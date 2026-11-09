using UnityEngine;

/// <summary>A room destination with an ordered set of storage positions.</summary>
public sealed class DeliveryDestination : MonoBehaviour
{
    [SerializeField] private Transform approachPoint;
    [SerializeField, Min(0)] private int capacity = 4;
    [SerializeField] private Transform[] storageSlots;

    public Transform ApproachPoint => approachPoint != null ? approachPoint : transform;
    public int ConfiguredCapacity => capacity;
    public int SlotCount => storageSlots == null ? 0 : storageSlots.Length;
    public int OccupiedCount
    {
        get
        {
            if (storageSlots == null) return 0;

            var occupied = 0;
            foreach (var slot in storageSlots)
            {
                if (slot != null && slot.GetComponentInChildren<PackageItem>(true) != null)
                {
                    occupied++;
                }
            }

            return occupied;
        }
    }

    public int AvailableCapacity => Mathf.Max(0, Mathf.Min(capacity, SlotCount) - OccupiedCount);
    public bool HasCapacity => AvailableCapacity > 0;

    public bool TryStore(PackageItem package)
    {
        if (package == null || !HasCapacity || storageSlots == null)
        {
            return false;
        }

        foreach (var slot in storageSlots)
        {
            if (slot == null || slot.GetComponentInChildren<PackageItem>(true) != null)
            {
                continue;
            }

            package.transform.SetParent(slot, false);
            package.transform.localPosition = Vector3.zero;
            package.transform.localRotation = Quaternion.identity;
            package.SetState(PackageState.Delivered);
            return true;
        }

        return false;
    }

    public int ClearAll()
    {
        if (storageSlots == null) return 0;

        var removed = 0;
        foreach (var slot in storageSlots)
        {
            if (slot == null) continue;
            foreach (var package in slot.GetComponentsInChildren<PackageItem>(true))
            {
                Destroy(package.gameObject);
                removed++;
            }
        }

        return removed;
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(0, capacity);
    }
}
