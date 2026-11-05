using NUnit.Framework;
using UnityEngine;

public sealed class DeliveryDestinationTests
{
    private GameObject destinationObject;
    private DeliveryDestination destination;
    private Transform[] slots;

    [SetUp]
    public void SetUp()
    {
        destinationObject = new GameObject("Destination");
        destination = destinationObject.AddComponent<DeliveryDestination>();
        slots = new Transform[2];
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = new GameObject($"Slot_{i + 1}");
            slot.transform.SetParent(destinationObject.transform);
            slots[i] = slot.transform;
        }

        SetPrivateField("capacity", 2);
        SetPrivateField("storageSlots", slots);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(destinationObject);
    }

    [Test]
    public void AvailableCapacity_IsLimitedByConfiguredCapacityAndSlots()
    {
        SetPrivateField("capacity", 4);

        Assert.That(destination.AvailableCapacity, Is.EqualTo(2));
        Assert.That(destination.HasCapacity, Is.True);
    }

    [Test]
    public void TryStore_PlacesPackageInFirstFreeSlotAndMarksDelivered()
    {
        var package = CreatePackage("Package");

        Assert.That(destination.TryStore(package), Is.True);
        Assert.That(package.transform.parent, Is.EqualTo(slots[0]));
        Assert.That(package.State, Is.EqualTo(PackageState.Delivered));
        Assert.That(destination.OccupiedCount, Is.EqualTo(1));
        Assert.That(destination.AvailableCapacity, Is.EqualTo(1));
    }

    [Test]
    public void TryStore_ReturnsFalseForNullOrWhenFull()
    {
        Assert.That(destination.TryStore(null), Is.False);
        Assert.That(destination.TryStore(CreatePackage("Package_1")), Is.True);
        Assert.That(destination.TryStore(CreatePackage("Package_2")), Is.True);
        Assert.That(destination.TryStore(CreatePackage("Package_3")), Is.False);
        Assert.That(destination.OccupiedCount, Is.EqualTo(2));
    }

    private PackageItem CreatePackage(string packageName)
    {
        var packageObject = new GameObject(packageName);
        return packageObject.AddComponent<PackageItem>();
    }

    private void SetPrivateField(string fieldName, object value)
    {
        var field = typeof(DeliveryDestination).GetField(fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected serialized field '{fieldName}'.");
        field.SetValue(destination, value);
    }
}
