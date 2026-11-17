using UnityEngine;

namespace MordomoProto.Fisica
{
    // Gira os 4 pivôs individuais (eixo local X) conforme a velocidade de CADA lado.
    // Rodas à esquerda (x<0) usam a velocidade esquerda; à direita, a direita.
    // Raio vem da configuração do corpo (0,14 m).
    public sealed class WheelVisualSync : MonoBehaviour
    {
        public KinematicDriveBody body;
        public Transform frontLeft, frontRight, rearLeft, rearRight;

        public float LastLeftAngleDeg { get; private set; }
        public float LastRightAngleDeg { get; private set; }

        void LateUpdate() { Sync(); }

        public void Sync()
        {
            if (body == null) return;
            var m = body.Model;
            double r = m.Parameters.WheelRadius;
            float extra = Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime);
            double l = m.LeftAngle + m.LeftSpeed / r * extra;
            double rr = m.RightAngle + m.RightSpeed / r * extra;
            LastLeftAngleDeg = (float)(l * Mathf.Rad2Deg % 360.0);
            LastRightAngleDeg = (float)(rr * Mathf.Rad2Deg % 360.0);
            Apply(frontLeft, LastLeftAngleDeg);
            Apply(rearLeft, LastLeftAngleDeg);
            Apply(frontRight, LastRightAngleDeg);
            Apply(rearRight, LastRightAngleDeg);
        }

        static void Apply(Transform t, float deg)
        {
            if (t != null) t.localRotation = Quaternion.AngleAxis(deg, Vector3.right);
        }
    }
}
