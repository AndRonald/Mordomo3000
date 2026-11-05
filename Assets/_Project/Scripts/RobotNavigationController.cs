using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal test controller for sending the robot to configured scene destinations.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
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

    private NavMeshAgent agent;
    private bool hasActiveDestination;

    /// <summary>Raised once when the current navigation request reaches its destination.</summary>
    public event Action Arrived;

    public bool KeyboardShortcutsEnabled
    {
        get => keyboardShortcutsEnabled;
        set => keyboardShortcutsEnabled = value;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void Update()
    {
        if (keyboardShortcutsEnabled)
        {
            ReadTestShortcuts();
        }

        CheckArrival();
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

        hasActiveDestination = true;
        Debug.Log($"Robot navigation started toward {hit.position}.", this);
        return true;
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
        if (!hasActiveDestination || agent == null || !agent.enabled || agent.pathPending)
        {
            return;
        }

        if (agent.pathStatus != NavMeshPathStatus.PathComplete ||
            agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.1f) ||
            agent.velocity.sqrMagnitude > 0.01f)
        {
            return;
        }

        hasActiveDestination = false;
        Debug.Log("Robot reached its destination.", this);
        Arrived?.Invoke();
    }
}
