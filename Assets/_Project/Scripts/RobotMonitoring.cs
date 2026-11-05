using UnityEngine;

/// <summary>Writes delivery and navigation state changes to the Unity Console.</summary>
public sealed class RobotMonitoring : MonoBehaviour
{
    private PackageDeliveryManager deliveryManager;
    private RobotNavigationController navigationController;

    public void Initialize(PackageDeliveryManager manager, RobotNavigationController navigation)
    {
        Unsubscribe();
        deliveryManager = manager;
        navigationController = navigation;

        if (deliveryManager != null)
        {
            deliveryManager.DeliveryStateChanged += HandleDeliveryStateChanged;
            deliveryManager.PackageDelivered += HandlePackageDelivered;
        }

        if (navigationController != null)
        {
            navigationController.NavigationStateChanged += HandleNavigationStateChanged;
        }

        if (deliveryManager != null)
        {
            HandleDeliveryStateChanged(deliveryManager.CurrentState);
        }

        if (navigationController != null)
        {
            HandleNavigationStateChanged(navigationController.CurrentNavigationState);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void HandleDeliveryStateChanged(PackageDeliveryManager.DeliveryState state)
    {
        if (deliveryManager == null)
        {
            return;
        }

        Debug.Log(
            $"[RobotMonitor] Delivery state: {state}; package={deliveryManager.ActivePackageId}; " +
            $"destination={deliveryManager.ActiveDestinationName}; elapsed={deliveryManager.ActiveTaskElapsedSeconds:F1}s; " +
            $"capacity=[{deliveryManager.GetDestinationCapacitySummary()}].",
            deliveryManager);
    }

    private void HandlePackageDelivered(int packageId, string destinationName, float elapsedSeconds, string capacitySummary)
    {
        Debug.Log(
            $"[RobotMonitor] Delivered package {packageId} to {destinationName} in {elapsedSeconds:F1}s. " +
            $"Capacity: {capacitySummary}.",
            this);
    }

    private void HandleNavigationStateChanged(RobotNavigationState state)
    {
        if (navigationController == null)
        {
            return;
        }

        Debug.Log(
            $"[RobotMonitor] Navigation state: {state}; destination={navigationController.CurrentDestinationPosition}.",
            navigationController);
    }

    private void Unsubscribe()
    {
        if (deliveryManager != null)
        {
            deliveryManager.DeliveryStateChanged -= HandleDeliveryStateChanged;
            deliveryManager.PackageDelivered -= HandlePackageDelivered;
        }

        if (navigationController != null)
        {
            navigationController.NavigationStateChanged -= HandleNavigationStateChanged;
        }
    }
}
