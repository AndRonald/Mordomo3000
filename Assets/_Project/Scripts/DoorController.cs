using UnityEngine;
using UnityEngine.AI;

/// <summary>Swings a door leaf around its hinge between closedAngle and openAngle. The obstacle blocks the NavMesh only while the door is closed.</summary>
public sealed class DoorController : MonoBehaviour
{
    [SerializeField] private Transform leaf;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField] private float closedAngle = 0f;
    [SerializeField] private float openAngle = 90f;
    [SerializeField, Min(1f)] private float speed = 120f;
    [SerializeField] private bool startOpen = true;

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
        if (leaf != null)
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

        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, speed * Time.deltaTime);
        leaf.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
    }
}
