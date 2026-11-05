using System;
using UnityEngine;

/// <summary>Simulated short-range sensors used to detect obstacles in front of the robot.</summary>
public sealed class RobotSensorSuite : MonoBehaviour
{
    [Header("Front obstacle sensor")]
    [SerializeField] private Vector3 sensorOriginOffset = new Vector3(0f, 0.1f, 0.3f);
    [SerializeField, Min(0.05f)] private float detectionDistance = 0.75f;
    [SerializeField, Min(0.01f)] private float detectionRadius = 0.12f;
    [SerializeField, Min(0.02f)] private float scanInterval = 0.1f;
    [SerializeField] private LayerMask obstacleLayers = ~0;

    private float nextScanTime;
    private Collider detectedObstacle;

    /// <summary>Raised when the obstacle in front changes. A null collider means the path is clear.</summary>
    public event Action<bool, Collider> ObstacleChanged;

    public bool HasObstacleAhead => detectedObstacle != null;
    public Collider DetectedObstacle => detectedObstacle;
    public float DetectionDistance => detectionDistance;

    private void Update()
    {
        if (Time.time < nextScanTime)
        {
            return;
        }

        nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
        ScanNow();
    }

    /// <summary>Performs one sensor scan immediately. Useful for deterministic simulation checks.</summary>
    public void ScanNow()
    {
        var origin = transform.TransformPoint(sensorOriginOffset);
        var direction = transform.forward;
        var hits = Physics.SphereCastAll(
            origin,
            Mathf.Max(0.01f, detectionRadius),
            direction,
            Mathf.Max(0.05f, detectionDistance),
            obstacleLayers,
            QueryTriggerInteraction.Ignore);

        Collider closestObstacle = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestObstacle = hit.collider;
            }
        }

        if (ReferenceEquals(closestObstacle, detectedObstacle))
        {
            return;
        }

        detectedObstacle = closestObstacle;
        ObstacleChanged?.Invoke(detectedObstacle != null, detectedObstacle);
    }

    private void OnValidate()
    {
        detectionDistance = Mathf.Max(0.05f, detectionDistance);
        detectionRadius = Mathf.Max(0.01f, detectionRadius);
        scanInterval = Mathf.Max(0.02f, scanInterval);
    }

    private void OnDrawGizmos()
    {
        var origin = transform.TransformPoint(sensorOriginOffset);
        var end = origin + transform.forward * detectionDistance;
        Gizmos.color = Application.isPlaying && HasObstacleAhead ? Color.red : new Color(0.15f, 0.85f, 1f, 1f);
        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(origin, detectionRadius);
        Gizmos.DrawWireSphere(end, detectionRadius);
    }
}
