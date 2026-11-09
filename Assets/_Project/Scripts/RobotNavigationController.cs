using System;
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

    private NavMeshAgent agent;
    private RobotSensorSuite sensorSuite;
    private bool hasActiveDestination;
    private Vector3 currentDestinationPosition;
    private float nextRepathAttemptTime;
    private bool hasLoggedRepathFailure;
    private RobotNavigationState navigationState;
    private float currentForwardSpeed;

    /// <summary>Raised once when the current navigation request reaches its destination.</summary>
    public event Action Arrived;
    public event Action<RobotNavigationState> NavigationStateChanged;

    public RobotNavigationState CurrentNavigationState => navigationState;
    public Vector3 CurrentDestinationPosition => currentDestinationPosition;

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

            agent.isStopped = true;
            nextRepathAttemptTime = Time.time + obstacleRepathRetryInterval;
            hasLoggedRepathFailure = false;
            SetNavigationState(RobotNavigationState.WaitingForObstacle);
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
        if (NavMesh.CalculatePath(agent.transform.position, currentDestinationPosition, agent.areaMask, path) &&
            path.status == NavMeshPathStatus.PathComplete && agent.SetPath(path))
        {
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
