using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Opens and closes a door leaf. Hinged mode (default) swings the leaf around its hinge between closedAngle and
/// openAngle. Sliding mode moves the leaf in a straight line between two local positions (external rail door).
/// The obstacle blocks the NavMesh only while the door is closed. In sliding mode the leaf collider is disabled
/// while the leaf is fully open, so the parked panel never registers as an obstacle for the sensors.
/// </summary>
public sealed class DoorController : MonoBehaviour
{
    public enum DoorMode { Hinged, Sliding }

    [SerializeField] private Transform leaf;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField] private float closedAngle = 0f;
    [SerializeField] private float openAngle = 90f;
    [SerializeField, Min(1f)] private float speed = 120f;
    [SerializeField] private bool startOpen = true;

    [Header("Sliding mode")]
    [SerializeField] private DoorMode mode = DoorMode.Hinged;
    [SerializeField] private Vector3 slideClosedLocalPosition;
    [SerializeField] private Vector3 slideOpenLocalPosition;
    [SerializeField, Min(0.05f)] private float slideSpeed = 1.5f;
    [SerializeField] private Collider leafCollider;

    private float currentAngle;
    private float targetAngle;
    private bool isOpen;

    public bool IsOpen => isOpen;

    public void SetOpen(bool open)
    {
        isOpen = open;
        targetAngle = open ? openAngle : closedAngle;
        if (obstacle != null)
        {
            obstacle.enabled = !open;
        }
    }

    private void Awake()
    {
        SetOpen(startOpen);
        currentAngle = targetAngle;
        if (leaf == null)
        {
            return;
        }

        if (mode == DoorMode.Sliding)
        {
            leaf.localPosition = isOpen ? slideOpenLocalPosition : slideClosedLocalPosition;
            UpdateLeafCollider();
        }
        else
        {
            leaf.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
        }
    }

    private void Update()
    {
        if (leaf == null)
        {
            return;
        }

        if (mode == DoorMode.Sliding)
        {
            var target = isOpen ? slideOpenLocalPosition : slideClosedLocalPosition;
            leaf.localPosition = Vector3.MoveTowards(leaf.localPosition, target, slideSpeed * Time.deltaTime);
            UpdateLeafCollider();
            return;
        }

        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, speed * Time.deltaTime);
        leaf.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
    }

    private void UpdateLeafCollider()
    {
        if (leafCollider == null)
        {
            return;
        }

        var fullyOpen = isOpen && (leaf.localPosition - slideOpenLocalPosition).sqrMagnitude < 0.0001f;
        leafCollider.enabled = !fullyOpen;
    }
}
