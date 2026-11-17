using UnityEngine;

namespace MordomoProto.Fisica
{
    // Valida cada passo do KinematicDriveBody por varredura + sobreposição (sem callbacks).
    [DisallowMultipleComponent]
    public sealed class SweepCollisionGuard : MonoBehaviour
    {
        [Tooltip("Aumenta a pegada horizontalmente (nunca reduz). 0 = collider original.")]
        public float safetyMargin = 0f;
        public float contactSkin = 0.001f;
        public float maxLinearStep = 0.01f;
        public float maxAngularStepDeg = 2f;
        public float floorSkin = 0.02f;

        FootprintProbe _probe;

        public FootprintProbe Probe
        {
            get
            {
                if (_probe == null) _probe = new FootprintProbe(GetComponent<BoxCollider>(), transform);
                _probe.Margin = Mathf.Max(0f, safetyMargin);
                _probe.FloorSkin = floorSkin;
                return _probe;
            }
        }

        public float ComputeSafeFraction(Vector3 p0, Quaternion q0, Vector3 p1, Quaternion q1)
        {
            return Probe.SafeFraction(p0, q0, p1, q1, maxLinearStep, maxAngularStepDeg, contactSkin);
        }
    }
}
