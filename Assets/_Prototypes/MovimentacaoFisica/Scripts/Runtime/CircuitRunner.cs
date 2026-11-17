using System;
using System.Collections.Generic;
using UnityEngine;

namespace MordomoProto.Fisica
{
    // Executa cenários no circuito: seguimento de trajetória, giro no lugar, sensor + frenagem + recuperação.
    // Registra folga mínima, penetração máxima e precisão de trajetória.
    [DefaultExecutionOrder(-50)]
    public sealed class CircuitRunner : MonoBehaviour
    {
        public KinematicDriveBody body;

        [Header("Sensor (mesmos valores do RobotSensorSuite atual)")]
        public float sensorLookahead = 0.85f;   // alcance à frente do para-choque (atual: 0,75 m + raio 0,12 m)

        [Header("Parada / recuperação")]
        public float stopMargin = 0.30f;          // folga para-choque -> obstáculo ao parar
        public float recoveryRotateDeg = 30f;
        public int maxRecoveries = 3;
        public float lookahead = 0.35f;
        public float maxDuration = 120f;

        public enum Phase { Idle, Follow, Spin, Recover, Done }
        public Phase CurrentPhase { get; private set; }
        public bool Finished { get { return CurrentPhase == Phase.Done; } }
        public MotionMetrics Metrics { get; private set; }
        public bool SensorEnabled, RecoveryEnabled;
        public Func<Vector2, List<Vector2>> BypassPlanner;
        public readonly List<KeyValuePair<string, Bounds>> Zones = new List<KeyValuePair<string, Bounds>>();

        List<Vector2> _refPath;
        PathFollower _follower;
        bool _inBypass;
        float _t0, _blockedTimer;
        double _spinTarget, _spinStartYaw, _recTarget, _recStartYaw;
        Vector3 _spinStartPos, _lastPos;
        float _lastGapFront = float.PositiveInfinity;

        void Awake() { Metrics = new MotionMetrics(); }

        PathFollower MakeFollower(List<Vector2> pts)
        {
            var xs = new double[pts.Count]; var zs = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++) { xs[i] = pts[i].x; zs[i] = pts[i].y; }
            var cfg = body.config;
            var f = new PathFollower(xs, zs);
            f.Lookahead = lookahead;
            f.MaxSpeed = cfg.maxSpeed;
            f.MaxYawRate = cfg.maxYawRateDeg * Math.PI / 180.0;
            f.Braking = cfg.braking;
            f.EffectiveTrack = cfg.track * cfg.controllerChi;
            return f;
        }

        void ResetRun()
        {
            Metrics = new MotionMetrics();
            _t0 = Time.fixedTime;
            _blockedTimer = 0f;
            _inBypass = false;
            _lastPos = body.Position;
            _lastGapFront = float.PositiveInfinity;
        }

        public void BeginPath(List<Vector2> path, bool sensor, bool recovery)
        {
            ResetRun();
            _refPath = path;
            _follower = MakeFollower(path);
            SensorEnabled = sensor; RecoveryEnabled = recovery;
            CurrentPhase = Phase.Follow;
        }

        public void BeginSpin(float degrees)
        {
            ResetRun();
            _spinTarget = degrees * Math.PI / 180.0;
            _spinStartYaw = body.Model.UnwrappedYaw;
            _spinStartPos = body.Position;
            CurrentPhase = Phase.Spin;
        }

        void Finish(bool reached, string reason)
        {
            Metrics.Reached = reached;
            Metrics.FailReason = reason ?? "";
            Metrics.Duration = Time.fixedTime - _t0;
            Metrics.ContactStops = body.ContactStopCount;
            Metrics.AvgStepMicros = body.AverageStepMicroseconds;
            Metrics.MaxStepMicros = body.MaxStepMicroseconds;
            body.SetCommand(0f, 0f);
            CurrentPhase = Phase.Done;
        }

        void Measure()
        {
            var probe = body.guard.Probe;
            Vector3 pos = body.Position;
            Quaternion rot = body.Rotation;
            Metrics.Distance += (pos - _lastPos).magnitude; _lastPos = pos;
            Metrics.PeakSpeed = Math.Max(Metrics.PeakSpeed, Math.Abs(body.LinearSpeed));

            float clr = probe.MeasureClearance(pos, rot);
            if (clr < Metrics.MinClearance) Metrics.MinClearance = clr;
            float pen = probe.MeasurePenetration(pos, rot);
            if (pen > Metrics.MaxPenetration) Metrics.MaxPenetration = pen;

            foreach (var z in Zones)
            {
                if (pos.x >= z.Value.min.x && pos.x <= z.Value.max.x && pos.z >= z.Value.min.z && pos.z <= z.Value.max.z)
                {
                    double cur;
                    if (!Metrics.ZoneMinClearance.TryGetValue(z.Key, out cur)) cur = double.PositiveInfinity;
                    if (clr < cur) Metrics.ZoneMinClearance[z.Key] = clr;
                }
            }

            if (_refPath != null && !_inBypass && CurrentPhase == Phase.Follow)
            {
                double d = DistanceToPolyline(pos.x, pos.z);
                if (d > Metrics.CteMax) Metrics.CteMax = d;
                Metrics.CteSumSq += d * d; Metrics.CteCount++;
            }
        }

        double DistanceToPolyline(float px, float pz)
        {
            double best = double.MaxValue;
            for (int i = 0; i < _refPath.Count - 1; i++)
            {
                Vector2 a = _refPath[i], b = _refPath[i + 1];
                Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(new Vector2(px, pz) - a, ab) / l2) : 0f;
                Vector2 q = a + ab * t;
                double d = Vector2.Distance(q, new Vector2(px, pz));
                if (d < best) best = d;
            }
            return best;
        }

        // Varredura da PEGADA COMPLETA (0,55 x 0,65 m) ao longo da trajetória à frente.
        // O sensor atual do RobotSensorSuite (esfera r=0,12 m) não cobre a largura do corpo (meia-largura 0,275 m).
        // Retorna a distância (m) que o corpo ainda pode percorrer antes de sobrepor um obstáculo (inf = livre).
        float SenseGapAlongPath()
        {
            var probe = body.guard.Probe;
            const float step = 0.02f;
            for (float s = step; s <= sensorLookahead; s += step)
            {
                double x, z, yaw;
                _follower.PoseAhead(s, out x, out z, out yaw);
                Vector3 pos = new Vector3((float)x, 0f, (float)z);
                Quaternion rot = Quaternion.Euler(0f, (float)(yaw * Mathf.Rad2Deg), 0f);
                if (probe.OverlapsAt(pos, rot)) return Mathf.Max(0f, s - step);
            }
            return float.PositiveInfinity;
        }

        double YawControl(double remaining)
        {
            var cfg = body.config;
            double wmax = cfg.maxYawRateDeg * Math.PI / 180.0;
            double alphaB = 2.0 * cfg.braking / (cfg.track * cfg.controllerChi);
            double w = Math.Min(wmax, Math.Sqrt(2.0 * alphaB * 0.8 * Math.Abs(remaining)));
            return Math.Sign(remaining) * w;
        }

        void FixedUpdate()
        {
            if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Done) return;
            if (Time.fixedTime - _t0 > maxDuration) { Finish(false, "timeout"); return; }
            Measure();

            switch (CurrentPhase)
            {
                case Phase.Follow: StepFollow(); break;
                case Phase.Spin: StepSpin(); break;
                case Phase.Recover: StepRecover(); break;
            }
        }

        void StepFollow()
        {
            double vd, wd;
            _follower.Update(body.Pose, body.LinearSpeed, out vd, out wd);
            float v = (float)vd, w = (float)wd;

            if (SensorEnabled)
            {
                Vector3 pos = body.Position; Quaternion rot = body.Rotation;
                float gap = SenseGapAlongPath();
                _lastGapFront = gap;
                if (!float.IsPositiveInfinity(gap))
                {
                    float avail = Mathf.Max(0f, gap - stopMargin);
                    float vcap = Mathf.Sqrt(2f * body.config.braking * 0.8f * avail);
                    if (v > vcap)
                    {
                        if (v > 1e-6f) w *= vcap / v;
                        v = vcap;
                    }
                    if (Mathf.Abs(body.LinearSpeed) < 0.01f && gap <= stopMargin + 0.03f) _blockedTimer += Time.fixedDeltaTime;
                    else _blockedTimer = 0f;
                }
                else _blockedTimer = 0f;

                if (_blockedTimer > 0.3f)
                {
                    if (RecoveryEnabled && Metrics.Recoveries < maxRecoveries && BypassPlanner != null) { BeginRecover(pos, rot); return; }
                    if (_blockedTimer > 3f) { Finish(false, "bloqueado"); return; }
                }
            }

            body.SetCommand(v, w);
            if (_follower.Done)
            {
                Vector3 p = body.Position;
                Metrics.FinalGoalError = Vector2.Distance(new Vector2(p.x, p.z), new Vector2((float)_follower.GoalX, (float)_follower.GoalZ));
                Finish(true, null);
            }
        }

        void BeginRecover(Vector3 pos, Quaternion rot)
        {
            var probe = body.guard.Probe;
            Metrics.StopGapFront = _lastGapFront;
            // lado oposto ao obstáculo (obstáculo à direita => gira à esquerda)
            float sign = 1f;
            if (probe.LastBlocker != null)
            {
                Vector3 toObs = probe.LastBlocker.bounds.center - pos;
                float lateral = Vector3.Dot(toObs, rot * Vector3.right);
                sign = lateral > 0f ? -1f : 1f;
            }
            float deg = sign * recoveryRotateDeg;
            if (!probe.ArcIsClear(pos, rot, deg))
            {
                deg = -deg;
                if (!probe.ArcIsClear(pos, rot, deg)) { Finish(false, "arco bloqueado"); return; }
            }
            _recStartYaw = body.Model.UnwrappedYaw;
            _recTarget = deg * Math.PI / 180.0;
            CurrentPhase = Phase.Recover;
        }

        void StepRecover()
        {
            double done = body.Model.UnwrappedYaw - _recStartYaw;
            double rem = _recTarget - done;
            if (Math.Abs(rem) < 0.004 && Math.Abs(body.YawRate) < 0.01 && Math.Abs(body.LinearSpeed) < 0.01)
            {
                Vector3 p = body.Position;
                _follower = MakeFollower(BypassPlanner(new Vector2(p.x, p.z)));
                _inBypass = true;
                Metrics.Recoveries++;
                _blockedTimer = 0f;
                CurrentPhase = Phase.Follow;
                return;
            }
            body.SetCommand(0f, (float)YawControl(rem));
        }

        void StepSpin()
        {
            double done = body.Model.UnwrappedYaw - _spinStartYaw;
            double rem = _spinTarget - done;
            if (Math.Abs(rem) < 0.003 && Math.Abs(body.YawRate) < 0.01)
            {
                Metrics.SpinYawErrorDeg = (done - _spinTarget) * 180.0 / Math.PI;
                Metrics.SpinDrift = (body.Position - _spinStartPos).magnitude;
                Finish(true, null);
                return;
            }
            body.SetCommand(0f, (float)YawControl(rem));
        }
    }
}
