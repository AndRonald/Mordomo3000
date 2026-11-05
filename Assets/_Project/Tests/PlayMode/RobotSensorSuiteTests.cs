using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class RobotSensorSuiteTests
{
    private GameObject robotObject;
    private RobotSensorSuite sensor;

    [SetUp]
    public void SetUp()
    {
        robotObject = new GameObject("Robot");
        robotObject.AddComponent<BoxCollider>();
        sensor = robotObject.AddComponent<RobotSensorSuite>();
    }

    [TearDown]
    public void TearDown()
    {
        if (robotObject != null)
        {
            Object.Destroy(robotObject);
        }
    }

    [UnityTest]
    public IEnumerator ScanNow_ReportsObstacleAndClearPath()
    {
        var obstacleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacleObject.name = "SimulatedObstacle";
        obstacleObject.transform.position = new Vector3(0f, 0.1f, 0.9f);
        obstacleObject.transform.localScale = Vector3.one * 0.2f;
        Physics.SyncTransforms();

        var changeCount = 0;
        var lastBlockedState = false;
        sensor.ObstacleChanged += (blocked, _) =>
        {
            changeCount++;
            lastBlockedState = blocked;
        };

        sensor.ScanNow();
        Assert.That(sensor.HasObstacleAhead, Is.True);
        Assert.That(lastBlockedState, Is.True);

        Object.Destroy(obstacleObject);
        yield return null;
        Physics.SyncTransforms();
        sensor.ScanNow();

        Assert.That(sensor.HasObstacleAhead, Is.False);
        Assert.That(lastBlockedState, Is.False);
        Assert.That(changeCount, Is.EqualTo(2));
    }

    [Test]
    public void ScanNow_IgnoresRobotAndChildColliders()
    {
        var child = new GameObject("RobotFrontSensorHousing");
        child.transform.SetParent(robotObject.transform, false);
        child.transform.localPosition = new Vector3(0f, 0.1f, 0.6f);
        child.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        sensor.ScanNow();

        Assert.That(sensor.HasObstacleAhead, Is.False);
    }
}
