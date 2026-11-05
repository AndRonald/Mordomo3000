using NUnit.Framework;
using UnityEngine;

public sealed class PackageItemTests
{
    private GameObject packageObject;
    private PackageItem package;

    [SetUp]
    public void SetUp()
    {
        packageObject = new GameObject("Package");
        package = packageObject.AddComponent<PackageItem>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(packageObject);
    }

    [Test]
    public void Initialize_SetsIdAndWaitingState()
    {
        package.Initialize(17);

        Assert.That(package.PackageId, Is.EqualTo(17));
        Assert.That(package.State, Is.EqualTo(PackageState.WaitingForPickup));
    }

    [Test]
    public void SetState_UpdatesCurrentState()
    {
        package.SetState(PackageState.Carried);

        Assert.That(package.State, Is.EqualTo(PackageState.Carried));
    }
}
