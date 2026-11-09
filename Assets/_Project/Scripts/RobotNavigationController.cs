using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public enum RobotNavigationState
{
    Idle,
    Navigating,
    WaitingForObstacle,
    WaitingForRoute
}

/// <summary>
/// Minimal test controller for sending the robot to configured scene destinations.
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(RobotSensorSuite))]
public sealed class RobotNavigationController : MonoBehaviour
{
    [Header("Test destinations")]
    [SerializeField] private Transform pontoColeta;
    [SerializeField] private Transform sala;
    [SerializeField] private Transform cozinha;
    [SerializeField] private Transform quarto;
    [SerializeField] private Transform escritorio;

    [Header("Test input")]
    [SerializeField] private bool keyboardShortcutsEnabled = true;
    [SerializeField, Min(0.1f)] private float destinationSampleDistance = 0.5f;
    [SerializeField, Min(0.1f)] private float obstacleRepathRetryInterval = 1f;

    [Header("Differential drive movement")]
    [SerializeField, Min(0.01f)] private float forwardAcceleration = 0.7f;
    [SerializeField, Min(0.01f)] private float brakingDeceleration = 1f;
    [SerializeField, Range(0f, 90f)] private float fullSpeedHeadingAngle = 10f;
    [SerializeField, Range(1f, 180f)] private float turnInPlaceAngle = 65f;
    [SerializeField, Range(0f, 1f)] private float minimumTurningSpeedFraction = 0.25f;

    [Header("Obstacle recovery (in-place rotation while paused)")]
    [SerializeField] private bool recoveryEnabled = true;
    [SerializeField, Range(1f, 30f)] private float resumeHeadingTolerance = 10f;
    [SerializeField, Min(1)] private int maxRecoveryAttempts = 3;
    [SerializeField, Min(1f)] private float maxRotationDuration = 8f;
    [SerializeField, Min(0.1f)] private float recoverySiteRadius = 0.5f;
    [SerializeField, Range(0.005f, 0.04f)] private float arcCornerStep = 0.02f;
    [SerializeField, Range(0f, 0.05f)] private float collisionSkin = 0.01f;
    [Tooltip("Optional. Colliders treated as floor during the rotation check. Floor under the robot is also resolved with a downward raycast.")]
    [SerializeField] private Collider[] floorColliders;

    private NavMeshAgent agent;
    private RobotSensorSuite sensorSuite;
    private bool hasActiveDestination;
    private Vector3 currentDestinationPosition;
    private float nextRepathAttemptTime;
    private bool hasLoggedRepathFailure;
    private RobotNavigationState navigationState;
    private float currentForwardSpeed;

    private const int OverlapBufferSize = 32;
    private readonly Collider[] overlapBuffer = new Collider[OverlapBufferSize];
    private readonly HashSet<Collider> resolvedFloorColliders = new HashSet<Collider>();
    private bool recoveryRotating;
    private float recoverySense;
    private float recoveryRotationStartTime;
    private int recoveryAttempts;
    private Vector3 recoverySite;
    private bool recoverySiteValid;
    private bool recoveryExhausted;
    private bool recoveryRotatedThisEpisode;
    private bool recoveryLoggedAlignedBlocked;
    private bool recoveryLoggedPivotMismatch;
    private bool recoveryHasRoute;
    private Vector3 recoveryRouteDirection;
    private float nextRecoveryRouteTime;

    /// <summary>Raised once when the current navigation request reaches its destination.</summary>
    public event Action Arrived;
    public event Action<RobotNavigationState> NavigationStateChanged;

    public RobotNavigationState CurrentNavigationState => navigationState;
    public Vector3 CurrentDestinationPosition => currentDestinationPosition;

    /// <summary>Pause episodes in which the robot rotated in place to realign with its route.</summary>
    public int RecoveryRotationCount { get; private set; }
    /// <summary>Times navigation resumed after a recovery rotation.</summary>
    public int RecoveryResumeCount { get; private set; }
    /// <summary>Times recovery gave up (blocked arc, timeout or attempts exhausted).</summary>
    public int RecoveryFailureCount { get; private set; }

    public bool KeyboardShortcutsEnabled
    {
        get => keyboardShortcutsEnabled;
        set => keyboardShortcutsEnabled = value;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        sensorSuite = GetComponent<RobotSensorSuite>();
        if (sensorSuite == null)
        {
            Debug.LogError("Robot navigation requires a RobotSensorSuite component on the same GameObject.", this);
            enabled = false;
            return;
        }

        agent.updatePosition = false;
        agent.updateRotation = false;
        if (agent.isOnNavMesh)
        {
            agent.nextPosition = transform.position;
        }
    }

    private void OnEnable()
    {
        if (sensorSuite != null)
        {
            sensorSuite.ObstacleChanged += HandleObstacleChanged;
        }
    }

    private void OnDisable()
    {
        if (sensorSuite != null)
        {
            sensorSuite.ObstacleChanged -= HandleObstacleChanged;
        }
    }

    private void Update()
    {
        if (keyboardShortcutsEnabled)
        {
            ReadTestShortcuts();
        }

        RetryPathWhenClear();
        UpdateObstacleRecovery();
        UpdateDifferentialDriveMotion();
        CheckArrival();
    }

    private void UpdateDifferentialDriveMotion()
    {
        var deltaTime = Time.deltaTime;
        if (deltaTime <= 0f || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        var targetSpeed = 0f;
        if (hasActiveDestination && navigationState == RobotNavigationState.Navigating &&
            !agent.isStopped && !agent.pathPending)
        {
            var desiredDirection = agent.desiredVelocity;
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                var forward = transform.forward;
                forward.y = 0f;
                forward.Normalize();

                var direction = desiredDirection.normalized;
                var headingError = Mathf.Abs(Vector3.SignedAngle(forward, direction, Vector3.up));
                var targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    agent.angularSpeed * deltaTime);

                if (headingError < turnInPlaceAngle)
                {
                    var alignment = Mathf.InverseLerp(turnInPlaceAngle, fullSpeedHeadingAngle, headingError);
                    var speedFraction = Mathf.Lerp(minimumTurningSpeedFraction, 1f, alignment);
                    targetSpeed = agent.speed * speedFraction;
                }
            }
        }

        var speedChange = targetSpeed < currentForwardSpeed ? brakingDeceleration : forwardAcceleration;
        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, speedChange * deltaTime);

        if (currentForwardSpeed > 0.0001f)
        {
            agent.Move(transform.forward * (currentForwardSpeed * deltaTime));
        }

        transform.position = agent.nextPosition;
    }

    /// <summary>
    /// Requests a complete path to a destination. Returns false if the destination
    /// cannot be projected onto this agent's NavMesh or no complete path exists.
    /// </summary>
    public bool MoveTo(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            Debug.LogWarning("Robot navigation rejected: the agent is not on a NavMesh.", this);
            return false;
        }

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(destination, out var hit, destinationSampleDistance, filter))
        {
            Debug.LogWarning($"Robot navigation rejected: no nearby NavMesh at {destination}.", this);
            return false;
        }

        var path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning($"Robot navigation rejected: no complete path to {destination}.", this);
            return false;
        }

        if (!agent.SetPath(path))
        {
            Debug.LogWarning($"Robot navigation rejected: Unity could not set the path to {destination}.", this);
            return false;
        }

        currentDestinationPosition = hit.position;
        hasActiveDestination = true;
        hasLoggedRepathFailure = false;
        agent.isStopped = false;
        SetNavigationState(RobotNavigationState.Navigating);
        if (sensorSuite != null && sensorSuite.HasObstacleAhead)
        {
            HandleObstacleChanged(true, sensorSuite.DetectedObstacle);
        }

        ResetRecoveryProgress();
        Debug.Log($"Robot navigation started toward {hit.position}.", this);
        return true;
    }

    public bool HasCompletePathTo(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(destination, out var hit, destinationSampleDistance, filter))
        {
            return false;
        }

        var path = new NavMeshPath();
        return agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    public void SetKeyboardShortcutsEnabled(bool enabled)
    {
        keyboardShortcutsEnabled = enabled;
    }

    private void ReadTestShortcuts()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.digit1Key.wasPressedThisFrame) RequestDestination(pontoColeta, "Ponto_Coleta");
        if (keyboard.digit2Key.wasPressedThisFrame) RequestDestination(sala, "Sala");
        if (keyboard.digit3Key.wasPressedThisFrame) RequestDestination(cozinha, "Cozinha");
        if (keyboard.digit4Key.wasPressedThisFrame) RequestDestination(quarto, "Quarto");
        if (keyboard.digit5Key.wasPressedThisFrame) RequestDestination(escritorio, "Escritório");
    }

    private void RequestDestination(Transform destination, string label)
    {
        if (destination == null)
        {
            Debug.LogWarning($"Robot destination '{label}' is not assigned.", this);
            return;
        }

        MoveTo(destination.position);
    }

    private void CheckArrival()
    {
        if (!hasActiveDestination || navigationState != RobotNavigationState.Navigating ||
            agent == null || !agent.enabled || agent.pathPending)
        {
            return;
        }

        if (agent.pathStatus != NavMeshPathStatus.PathComplete ||
            agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.1f) ||
            currentForwardSpeed > 0.01f)
        {
            return;
        }

        hasActiveDestination = false;
        ResetRecoveryProgress();
        SetNavigationState(RobotNavigationState.Idle);
        Debug.Log("Robot reached its destination.", this);
        Arrived?.Invoke();
    }

    private void HandleObstacleChanged(bool isBlocked, Collider obstacle)
    {
        if (!hasActiveDestination || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (isBlocked)
        {
            var remainingDistance = Vector3.Distance(agent.nextPosition, currentDestinationPosition);
            if (sensorSuite != null && remainingDistance <= sensorSuite.DetectionDistance + agent.stoppingDistance)
            {
                return;
            }

            var wasWaitingForObstacle = navigationState == RobotNavigationState.WaitingForObstacle;
            agent.isStopped = true;
            nextRepathAttemptTime = Time.time + obstacleRepathRetryInterval;
            hasLoggedRepathFailure = false;
            SetNavigationState(RobotNavigationState.WaitingForObstacle);
            if (!wasWaitingForObstacle)
            {
                BeginPauseEpisode();
            }
            Debug.LogWarning($"Robot paused: obstacle '{obstacle.name}' detected ahead.", this);
            return;
        }

        if (navigationState == RobotNavigationState.WaitingForObstacle)
        {
            nextRepathAttemptTime = Time.time;
        }
    }

    private void RetryPathWhenClear()
    {
        if (!hasActiveDestination ||
            (navigationState != RobotNavigationState.WaitingForObstacle && navigationState != RobotNavigationState.WaitingForRoute) ||
            sensorSuite == null || sensorSuite.HasObstacleAhead || Time.time < nextRepathAttemptTime ||
            agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        nextRepathAttemptTime = Time.time + Mathf.Max(0.1f, obstacleRepathRetryInterval);
        var path = new NavMeshPath();
        var hasCompletePath = NavMesh.CalculatePath(agent.transform.position, currentDestinationPosition, agent.areaMask, path) &&
            path.status == NavMeshPathStatus.PathComplete;
        if (hasCompletePath && !IsAlignedForResume(path))
        {
            // Keep waiting: the in-place recovery rotation aligns the robot with the route first.
            return;
        }

        if (hasCompletePath && agent.SetPath(path))
        {
            if (recoveryRotatedThisEpisode)
            {
                RecoveryResumeCount++;
                recoveryRotatedThisEpisode = false;
                Debug.Log("Robot recovery: aligned with the route and resumed.", this);
            }

            agent.isStopped = false;
            hasLoggedRepathFailure = false;
            SetNavigationState(RobotNavigationState.Navigating);
            Debug.Log("Obstacle cleared; robot recalculated its route.", this);
            return;
        }

        agent.isStopped = true;
        SetNavigationState(RobotNavigationState.WaitingForRoute);
        if (!hasLoggedRepathFailure)
        {
            Debug.LogWarning("Robot remains paused: no complete route is available after the obstacle cleared.", this);
            hasLoggedRepathFailure = true;
        }
    }


    // ------------------------------------------------------------------
    // Obstacle recovery: rotate in place while paused, then resume only when aligned with the route.
    // ------------------------------------------------------------------

    private void BeginPauseEpisode()
    {
        var position = transform.position;
        if (!recoverySiteValid || HorizontalDistance(position, recoverySite) > recoverySiteRadius)
        {
            recoverySite = position;
            recoverySiteValid = true;
            recoveryAttempts = 0;
            recoveryExhausted = false;
        }

        recoveryAttempts++;
        recoveryRotating = false;
        recoveryRotatedThisEpisode = false;
        recoveryLoggedAlignedBlocked = false;
        recoveryLoggedPivotMismatch = false;
        recoveryHasRoute = false;
        nextRecoveryRouteTime = 0f;
        if (recoveryAttempts > maxRecoveryAttempts)
        {
            MarkRecoveryExhausted("too many pauses at the same spot");
        }
    }

    private void ResetRecoveryProgress()
    {
        recoverySiteValid = false;
        recoveryAttempts = 0;
        recoveryExhausted = false;
        recoveryRotating = false;
        recoveryRotatedThisEpisode = false;
    }

    private void MarkRecoveryExhausted(string reason)
    {
        recoveryRotating = false;
        if (recoveryExhausted)
        {
            return;
        }

        recoveryExhausted = true;
        RecoveryFailureCount++;
        Debug.LogWarning($"Robot recovery exhausted: {reason}. The robot stays paused.", this);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool IsAlignedForResume(NavMeshPath path)
    {
        if (!recoveryEnabled || !TryGetRouteDirection(path, agent.nextPosition, out var direction))
        {
            return true;
        }

        var forward = transform.forward;
        forward.y = 0f;
        return Mathf.Abs(Vector3.SignedAngle(forward.normalized, direction, Vector3.up)) <= resumeHeadingTolerance;
    }

    private static bool TryGetRouteDirection(NavMeshPath path, Vector3 start, out Vector3 direction)
    {
        direction = Vector3.zero;
        var corners = path.corners;
        for (var i = 0; i < corners.Length; i++)
        {
            var delta = corners[i] - start;
            delta.y = 0f;
            if (delta.magnitude > 0.1f)
            {
                direction = delta.normalized;
                return true;
            }
        }

        return false;
    }

    private bool ComputeRouteDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(agent.nextPosition, currentDestinationPosition, agent.areaMask, path) &&
               path.status == NavMeshPathStatus.PathComplete &&
               TryGetRouteDirection(path, agent.nextPosition, out direction);
    }

    private void UpdateObstacleRecovery()
    {
        if (!recoveryEnabled || !hasActiveDestination || navigationState != RobotNavigationState.WaitingForObstacle ||
            agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            recoveryRotating = false;
            return;
        }

        var deltaTime = Time.deltaTime;
        if (recoveryExhausted || deltaTime <= 0f || currentForwardSpeed > 0.02f)
        {
            return;
        }

        // The physical pivot is transform.position (what the collider is attached to). It must match the agent position.
        if (HorizontalDistance(transform.position, agent.nextPosition) > 0.02f)
        {
            if (!recoveryLoggedPivotMismatch)
            {
                recoveryLoggedPivotMismatch = true;
                Debug.LogWarning("Robot recovery skipped: transform.position and agent.nextPosition differ.", this);
            }

            return;
        }

        if (Time.time >= nextRecoveryRouteTime)
        {
            nextRecoveryRouteTime = Time.time + 0.25f;
            recoveryHasRoute = ComputeRouteDirection(out recoveryRouteDirection);
        }

        if (!recoveryHasRoute)
        {
            return;
        }

        var forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();
        var error = Vector3.SignedAngle(forward, recoveryRouteDirection, Vector3.up);
        if (Mathf.Abs(error) <= resumeHeadingTolerance * 0.5f)
        {
            if (recoveryRotating)
            {
                recoveryRotating = false;
                nextRepathAttemptTime = Time.time;
            }

            if (sensorSuite.HasObstacleAhead && !recoveryLoggedAlignedBlocked)
            {
                recoveryLoggedAlignedBlocked = true;
                Debug.Log("Robot recovery: aligned with the route but the way ahead is still blocked; waiting.", this);
            }

            return;
        }

        if (!recoveryRotating)
        {
            ResolveFloorColliderUnderRobot();
            var sense = Mathf.Sign(error);
            var shortArc = Mathf.Abs(error);
            if (ArcIsClear(sense * shortArc))
            {
                recoverySense = sense;
            }
            else if (ArcIsClear(-sense * (360f - shortArc)))
            {
                recoverySense = -sense;
            }
            else
            {
                MarkRecoveryExhausted("rotation arc is blocked in both directions");
                return;
            }

            recoveryRotating = true;
            recoveryRotationStartTime = Time.time;
            recoveryRotatedThisEpisode = true;
            RecoveryRotationCount++;
            Debug.Log($"Robot recovery: rotating in place to realign with the route (heading error {error:0}°).", this);
        }

        if (Time.time - recoveryRotationStartTime > maxRotationDuration)
        {
            MarkRecoveryExhausted("rotation timed out");
            return;
        }

        var remaining = recoverySense > 0f ? Mathf.Repeat(error, 360f) : Mathf.Repeat(-error, 360f);
        if (remaining > 340f)
        {
            // Overshot because the route direction changed; plan again next frame.
            recoveryRotating = false;
            return;
        }

        var step = Mathf.Min(agent.angularSpeed * deltaTime, remaining);
        if (!ArcIsClear(recoverySense * Mathf.Min(remaining, step + 3f)))
        {
            MarkRecoveryExhausted("rotation arc became blocked");
            return;
        }

        transform.rotation = Quaternion.AngleAxis(recoverySense * step, Vector3.up) * transform.rotation;
    }

    private void ResolveFloorColliderUnderRobot()
    {
        var pivot = transform.position;
        var hits = Physics.RaycastAll(pivot + Vector3.up * 0.5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore);
        Collider nearest = null;
        var nearestDistance = float.PositiveInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearest = hit.collider;
            }
        }

        if (nearest != null && nearest.bounds.max.y <= pivot.y + 0.005f)
        {
            resolvedFloorColliders.Add(nearest);
        }
    }

    private bool IsIgnorableFloor(Collider candidate, float pivotY)
    {
        if (candidate.bounds.max.y > pivotY + 0.005f)
        {
            return false;
        }

        if (resolvedFloorColliders.Contains(candidate))
        {
            return true;
        }

        if (floorColliders != null)
        {
            for (var i = 0; i < floorColliders.Length; i++)
            {
                if (floorColliders[i] == candidate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sweeps the robot's real box colliders through a yaw rotation about the pivot (transform.position) in small
    /// steps so that no corner travels more than arcCornerStep between two checks. Any incomplete query counts as blocked.
    /// </summary>
    public bool ArcIsClear(float totalDegrees)
    {
        var magnitude = Mathf.Abs(totalDegrees);
        if (magnitude < 0.01f)
        {
            return true;
        }

        var boxes = new List<BoxCollider>();
        foreach (var robotCollider in GetComponentsInChildren<Collider>(false))
        {
            if (!robotCollider.enabled || robotCollider.isTrigger)
            {
                continue;
            }

            if (robotCollider is BoxCollider box)
            {
                boxes.Add(box);
            }
            else
            {
                return false; // Unsupported footprint shape: cannot verify, treat as blocked.
            }
        }

        if (boxes.Count == 0)
        {
            return false;
        }

        var pivot = transform.position;
        var centers = new Vector3[boxes.Count];
        var halves = new Vector3[boxes.Count];
        var rotations = new Quaternion[boxes.Count];
        var maxRadius = 0.05f;
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var scale = box.transform.lossyScale;
            var half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            var center = box.transform.TransformPoint(box.center);
            maxRadius = Mathf.Max(maxRadius, HorizontalDistance(center, pivot) + new Vector2(half.x, half.z).magnitude);

            // Raise the bottom by 0.02 m (top unchanged) so resting on the floor is not an overlap; widen horizontally by the skin.
            half.y = Mathf.Max(0.01f, half.y - 0.01f);
            center += Vector3.up * 0.01f;
            half.x += collisionSkin;
            half.z += collisionSkin;

            centers[i] = center;
            halves[i] = half;
            rotations[i] = box.transform.rotation;
        }

        var stepDegrees = Mathf.Rad2Deg * arcCornerStep / maxRadius;
        var samples = Mathf.CeilToInt(magnitude / stepDegrees);
        var sign = Mathf.Sign(totalDegrees);
        for (var s = 1; s <= samples; s++)
        {
            var angle = sign * Mathf.Min(magnitude, s * stepDegrees);
            var q = Quaternion.AngleAxis(angle, Vector3.up);
            for (var i = 0; i < boxes.Count; i++)
            {
                var sampleCenter = pivot + q * (centers[i] - pivot);
                var count = Physics.OverlapBoxNonAlloc(sampleCenter, halves[i], overlapBuffer, q * rotations[i], ~0, QueryTriggerInteraction.Ignore);
                if (count >= overlapBuffer.Length)
                {
                    return false; // Possibly omitted results: never allow a rotation on an incomplete query.
                }

                for (var k = 0; k < count; k++)
                {
                    var hit = overlapBuffer[k];
                    if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (IsIgnorableFloor(hit, pivot.y))
                    {
                        continue;
                    }

                    return false;
                }
            }
        }

        return true;
    }

    private void SetNavigationState(RobotNavigationState newState)
    {
        if (navigationState == newState)
        {
            return;
        }

        navigationState = newState;
        NavigationStateChanged?.Invoke(navigationState);
    }
}
