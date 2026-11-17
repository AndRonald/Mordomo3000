using UnityEngine;

namespace MordomoProto.Fisica
{
    // Monta o corpo físico do protótipo (Rigidbody cinemático + collider ORIGINAL + guarda + corpo).
    public static class RobotRig
    {
        public static readonly Vector3 ColliderSize = new Vector3(0.55f, 1.10f, 0.65f);
        public static readonly Vector3 ColliderCenter = new Vector3(0f, 0.55f, 0f);

        public static KinematicDriveBody Configure(GameObject go, DriveConfig cfg)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.size = ColliderSize;
            box.center = ColliderCenter;

            var guard = go.GetComponent<SweepCollisionGuard>();
            if (guard == null) guard = go.AddComponent<SweepCollisionGuard>();

            var body = go.GetComponent<KinematicDriveBody>();
            if (body == null) body = go.AddComponent<KinematicDriveBody>();
            body.guard = guard;
            body.ApplyConfig(cfg != null ? cfg : new DriveConfig());
            return body;
        }

        public static KinematicDriveBody CreateBare(string name, Vector3 pos, float yawDeg, DriveConfig cfg)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            var body = Configure(go, cfg);
            body.Teleport(pos, yawDeg);
            return body;
        }
    }
}
