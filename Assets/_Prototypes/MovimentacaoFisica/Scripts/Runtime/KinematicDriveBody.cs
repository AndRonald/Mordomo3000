using System.Diagnostics;
using UnityEngine;

namespace MordomoProto.Fisica
{
    // Corpo físico do protótipo: Rigidbody cinemático movido no FixedUpdate com MovePosition/MoveRotation.
    // O movimento só é confirmado depois que o SweepCollisionGuard valida a varredura do passo.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KinematicDriveBody : MonoBehaviour
    {
        public DriveConfig config = new DriveConfig();
        public SweepCollisionGuard guard;

        Rigidbody _rb;
        DifferentialDriveModel _model;
        float _cmdV, _cmdYawRate;
        bool _wasStopped;

        public bool ContactStopped { get; private set; }
        public int ContactStopCount { get; private set; }
        public double LastStepMicroseconds { get; private set; }
        public double MaxStepMicroseconds { get; private set; }
        public double TotalStepMicroseconds { get; private set; }
        public long StepCount { get; private set; }

        public DifferentialDriveModel Model { get { Init(); return _model; } }
        public Pose2D Pose { get { return Model.Pose; } }
        public Rigidbody Body { get { Init(); return _rb; } }
        public float LinearSpeed { get { return (float)Model.LinearSpeed; } }
        public float YawRate { get { return (float)Model.YawRate; } }
        public Vector3 Position { get { var p = Pose; return new Vector3((float)p.X, Body.position.y, (float)p.Z); } }
        public Quaternion Rotation { get { return Quaternion.Euler(0f, (float)(Pose.Yaw * Mathf.Rad2Deg), 0f); } }
        public float AverageStepMicroseconds { get { return StepCount > 0 ? (float)(TotalStepMicroseconds / StepCount) : 0f; } }

        void Awake() { Init(); }

        void Init()
        {
            if (_rb != null) return;
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (guard == null) guard = GetComponent<SweepCollisionGuard>();
            string why = null;
            if (config == null || !config.ToParameters().IsValid(out why))
            {
                UnityEngine.Debug.LogWarning("[KinematicDriveBody] config inválida no Inspector; usando valores padrão.");
                config = new DriveConfig();
            }
            _model = new DifferentialDriveModel(config.ToParameters());
            SyncPoseFromTransform();
        }

        // Fase S: rejeita configuração impossível (mantém a anterior). Retorna se foi aplicada.
        public bool ApplyConfig(DriveConfig c)
        {
            Init();
            string why = null;
            if (c == null || !c.ToParameters().IsValid(out why))
            {
                UnityEngine.Debug.LogWarning("[KinematicDriveBody] ApplyConfig rejeitado: parâmetro inválido (" + (c == null ? "null" : why) + ").");
                return false;
            }
            config = c;
            _model.Parameters = c.ToParameters();
            return true;
        }

        void SyncPoseFromTransform()
        {
            Vector3 p = transform.position;
            _model.SetPose(new Pose2D(p.x, p.z, transform.eulerAngles.y * Mathf.Deg2Rad));
            _model.ResetMotion();
        }

        static bool Finito(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        // Fase S: pose não finita é rejeitada (nada muda). Retorna se o teleporte foi aplicado.
        public bool Teleport(Vector3 pos, float yawDeg)
        {
            Init();
            if (!Finito(pos.x) || !Finito(pos.y) || !Finito(pos.z) || !Finito(yawDeg))
            {
                UnityEngine.Debug.LogWarning("[KinematicDriveBody] Teleport rejeitado: pose não finita.");
                return false;
            }
            Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
            transform.SetPositionAndRotation(pos, q);
            _rb.position = pos;
            _rb.rotation = q;
            Physics.SyncTransforms();
            _model.SetPose(new Pose2D(pos.x, pos.z, yawDeg * Mathf.Deg2Rad));
            _model.ResetMotion();
            _cmdV = 0f; _cmdYawRate = 0f;
            _wasStopped = false; ContactStopped = false;
            return true;
        }

        // v em m/s (frente +), yawRate em rad/s (positivo = giro à direita, como o yaw do Unity).
        public void SetCommand(float v, float yawRate)
        {
            _cmdV = float.IsNaN(v) ? 0f : v;               // NaN -> parada segura; o modelo satura infinitos
            _cmdYawRate = float.IsNaN(yawRate) ? 0f : yawRate;
        }

        void FixedUpdate()
        {
            Init();
            long t0 = Stopwatch.GetTimestamp();
            double dt = Time.fixedDeltaTime;

            StepPlan plan = _model.Plan(_cmdV, _cmdYawRate, dt);
            Pose2D cur = _model.Pose;
            float y = _rb.position.y;
            Vector3 p0 = new Vector3((float)cur.X, y, (float)cur.Z);
            Quaternion q0 = Quaternion.Euler(0f, (float)(cur.Yaw * Mathf.Rad2Deg), 0f);
            Vector3 p1 = new Vector3((float)plan.End.X, y, (float)plan.End.Z);
            Quaternion q1 = Quaternion.Euler(0f, (float)(plan.End.Yaw * Mathf.Rad2Deg), 0f);

            float frac = 1f;
            if (guard != null) frac = guard.ComputeSafeFraction(p0, q0, p1, q1);
            _model.Commit(plan, frac);

            ContactStopped = frac < 0.999f;
            if (ContactStopped && !_wasStopped) ContactStopCount++;
            _wasStopped = ContactStopped;

            Pose2D np = _model.Pose;
            _rb.MovePosition(new Vector3((float)np.X, y, (float)np.Z));
            _rb.MoveRotation(Quaternion.Euler(0f, (float)(np.Yaw * Mathf.Rad2Deg), 0f));

            double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
            LastStepMicroseconds = us;
            TotalStepMicroseconds += us;
            StepCount++;
            if (us > MaxStepMicroseconds) MaxStepMicroseconds = us;
        }
    }
}
