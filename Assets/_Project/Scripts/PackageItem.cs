using UnityEngine;

public enum PackageState
{
    WaitingForPickup,
    Carried,
    Delivered
}

/// <summary>Runtime data attached to one simulated delivery package.</summary>
public sealed class PackageItem : MonoBehaviour
{
    [SerializeField] private int packageId;
    [SerializeField] private PackageState state = PackageState.WaitingForPickup;

    public int PackageId => packageId;
    public PackageState State => state;

    public void Initialize(int id)
    {
        packageId = id;
        state = PackageState.WaitingForPickup;
    }

    public void SetState(PackageState newState)
    {
        state = newState;
    }
}
