using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Coordinates package intake, pickup, room selection, delivery, and return.</summary>
public sealed class PackageDeliveryManager : MonoBehaviour
{
    private enum DeliveryState
    {
        Idle,
        GoingToPickup,
        GoingToDestination,
        Returning,
        WaitingForCapacity
    }

    [Header("Scene references")]
    [SerializeField] private RobotNavigationController robot;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform packageSpawnAnchor;
    [SerializeField] private Transform carryAnchor;
    [SerializeField] private Transform waitPoint;
    [SerializeField] private GameObject packagePrefab;

    [Header("Destination priority")]
    [Tooltip("Destinations are checked in this order; the first with available capacity is selected.")]
    [SerializeField] private DeliveryDestination[] destinations;
    [Tooltip("Scene object names used when no explicit destination references are assigned.")]
    [SerializeField] private string[] destinationPriority =
    {
        "Entrega_Sala",
        "Entrega_Cozinha",
        "Entrega_Quarto",
        "Entrega_Escritorio"
    };

    [Header("Package intake")]
    [SerializeField, Min(1)] private int maxWaitingPackages = 6;
    [SerializeField, Min(0.05f)] private float packageQueueSpacing = 0.35f;
    [SerializeField] private bool keyboardInputEnabled = true;
    [SerializeField, Min(0.1f)] private float retryInterval = 1f;

    private readonly List<PackageItem> waitingPackages = new List<PackageItem>();
    private DeliveryState state = DeliveryState.Idle;
    private PackageItem activePackage;
    private DeliveryDestination activeDestination;
    private Vector3 returnPosition;
    private int nextPackageId = 1;
    private float nextRetryTime;
    private bool warnedAboutFullCapacity;

    private void Awake()
    {
        if (robot == null)
        {
            robot = FindAnyObjectByType<RobotNavigationController>();
        }

        if (pickupPoint == null)
        {
            pickupPoint = FindSceneTransform("Casa/Central_Recebimento/Ponto_Coleta");
        }

        if (packageSpawnAnchor == null)
        {
            packageSpawnAnchor = FindSceneTransform("Casa/Central_Recebimento/Ancora_Spawn");
        }

        if (carryAnchor == null && robot != null)
        {
            carryAnchor = robot.transform.Find("Ponto_Carga");
        }

        if (destinations == null || destinations.Length == 0)
        {
            ResolveDestinationsByPriority();
        }

        if (robot != null)
        {
            robot.SetKeyboardShortcutsEnabled(false);
        }
    }

    private void OnEnable()
    {
        if (robot != null)
        {
            robot.Arrived += HandleRobotArrived;
        }
    }

    private void Start()
    {
        if (robot == null)
        {
            Debug.LogError("Package delivery is disabled: RobotNavigationController is not assigned.", this);
            enabled = false;
            return;
        }

        if (pickupPoint == null || packageSpawnAnchor == null || carryAnchor == null || packagePrefab == null)
        {
            Debug.LogError("Package delivery is disabled: assign pickup, spawn, carry, and package prefab references.", this);
            enabled = false;
            return;
        }

        returnPosition = waitPoint != null ? waitPoint.position : robot.transform.position;
        nextRetryTime = Time.time + 0.25f;
    }

    private void Update()
    {
        if (keyboardInputEnabled && Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            CreatePackage();
        }

        if ((state == DeliveryState.Idle || state == DeliveryState.WaitingForCapacity) && Time.time >= nextRetryTime)
        {
            TryStartNextPackage();
        }
    }

    private void OnDisable()
    {
        if (robot != null)
        {
            robot.Arrived -= HandleRobotArrived;
            robot.SetKeyboardShortcutsEnabled(true);
        }
    }

    public bool CreatePackage()
    {
        if (waitingPackages.Count >= maxWaitingPackages)
        {
            Debug.LogWarning($"The receiving station is full ({maxWaitingPackages} waiting packages).", this);
            return false;
        }

        var packageObject = Instantiate(packagePrefab, packageSpawnAnchor.position, packageSpawnAnchor.rotation);
        packageObject.name = $"Encomenda_{nextPackageId:000}";
        packageObject.transform.SetParent(packageSpawnAnchor, false);
        var package = packageObject.GetComponent<PackageItem>();
        if (package == null)
        {
            Debug.LogError("Package prefab is missing the PackageItem component.", packageObject);
            Destroy(packageObject);
            return false;
        }

        package.Initialize(nextPackageId++);
        waitingPackages.Add(package);
        LayoutWaitingPackages();
        Debug.Log($"Package {package.PackageId} added to the receiving queue.", package);

        if (state == DeliveryState.WaitingForCapacity)
        {
            nextRetryTime = Time.time;
        }

        return true;
    }

    private void TryStartNextPackage()
    {
        if (waitingPackages.Count == 0)
        {
            state = DeliveryState.Idle;
            warnedAboutFullCapacity = false;
            return;
        }

        if (!HasAvailableDestination())
        {
            if (!warnedAboutFullCapacity)
            {
                Debug.LogWarning("All delivery destinations are full. Packages remain queued until space is available.", this);
                warnedAboutFullCapacity = true;
            }

            state = DeliveryState.WaitingForCapacity;
            nextRetryTime = Time.time + retryInterval;
            return;
        }

        warnedAboutFullCapacity = false;
        activePackage = waitingPackages[0];
        activeDestination = null;
        if (!robot.MoveTo(pickupPoint.position))
        {
            state = DeliveryState.Idle;
            nextRetryTime = Time.time + retryInterval;
            return;
        }

        state = DeliveryState.GoingToPickup;
    }

    private bool HasAvailableDestination()
    {
        if (destinations == null) return false;

        foreach (var destination in destinations)
        {
            if (destination != null && destination.HasCapacity)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleRobotArrived()
    {
        switch (state)
        {
            case DeliveryState.GoingToPickup:
                CollectActivePackage();
                break;
            case DeliveryState.GoingToDestination:
                StoreActivePackage();
                break;
            case DeliveryState.Returning:
                FinishCycle();
                break;
        }
    }

    private void CollectActivePackage()
    {
        if (activePackage == null)
        {
            state = DeliveryState.Idle;
            nextRetryTime = Time.time + retryInterval;
            return;
        }

        waitingPackages.Remove(activePackage);
        activePackage.transform.SetParent(carryAnchor, false);
        activePackage.transform.localPosition = Vector3.zero;
        activePackage.transform.localRotation = Quaternion.identity;
        activePackage.SetState(PackageState.Carried);
        LayoutWaitingPackages();

        if (!TryRouteToDestination())
        {
            RequeueActivePackage();
            ReturnToWaitPoint();
            return;
        }

        Debug.Log($"Package {activePackage.PackageId} collected; heading to {activeDestination.name}.", activePackage);
    }

    private bool TryRouteToDestination()
    {
        if (destinations == null) return false;

        foreach (var destination in destinations)
        {
            if (destination == null || !destination.HasCapacity)
            {
                continue;
            }

            if (robot.MoveTo(destination.ApproachPoint.position))
            {
                activeDestination = destination;
                state = DeliveryState.GoingToDestination;
                return true;
            }
        }

        Debug.LogWarning("No available destination has a complete NavMesh path. Package was returned to the queue.", this);
        return false;
    }

    private void StoreActivePackage()
    {
        if (activePackage == null || activeDestination == null || !activeDestination.TryStore(activePackage))
        {
            if (!TryRouteToDestination())
            {
                RequeueActivePackage();
                ReturnToWaitPoint();
            }

            return;
        }

        Debug.Log($"Package {activePackage.PackageId} delivered to {activeDestination.name} ({activeDestination.OccupiedCount}/{activeDestination.ConfiguredCapacity}).", activePackage);
        activePackage = null;
        activeDestination = null;
        ReturnToWaitPoint();
    }

    private void RequeueActivePackage()
    {
        if (activePackage == null)
        {
            return;
        }

        var packageToRequeue = activePackage;
        packageToRequeue.SetState(PackageState.WaitingForPickup);
        packageToRequeue.transform.SetParent(packageSpawnAnchor, false);
        activePackage = null;
        activeDestination = null;
        waitingPackages.Insert(0, packageToRequeue);
        LayoutWaitingPackages();
    }

    private void ReturnToWaitPoint()
    {
        state = DeliveryState.Returning;
        if (!robot.MoveTo(waitPosition))
        {
            state = DeliveryState.Idle;
            nextRetryTime = Time.time + retryInterval;
        }
    }

    private Vector3 waitPosition => waitPoint != null ? waitPoint.position : returnPosition;

    private void FinishCycle()
    {
        state = DeliveryState.Idle;
        activePackage = null;
        activeDestination = null;
        nextRetryTime = Time.time + 0.25f;
    }

    private void LayoutWaitingPackages()
    {
        for (var i = 0; i < waitingPackages.Count; i++)
        {
            var package = waitingPackages[i];
            if (package == null) continue;
            package.transform.SetParent(packageSpawnAnchor, false);
            package.transform.localPosition = packageQueueSpacing * i * Vector3.right;
            package.transform.localRotation = Quaternion.identity;
        }
    }

    private static Transform FindSceneTransform(string path)
    {
        var sceneObject = GameObject.Find(path);
        return sceneObject != null ? sceneObject.transform : null;
    }

    private void ResolveDestinationsByPriority()
    {
        var available = FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include);
        var ordered = new List<DeliveryDestination>();
        if (destinationPriority != null)
        {
            foreach (var destinationName in destinationPriority)
            {
                foreach (var destination in available)
                {
                    if (destination != null && destination.name == destinationName && !ordered.Contains(destination))
                    {
                        ordered.Add(destination);
                        break;
                    }
                }
            }
        }

        destinations = ordered.ToArray();
    }
}
