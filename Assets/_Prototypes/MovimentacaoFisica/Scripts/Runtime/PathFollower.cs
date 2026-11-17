using System;

namespace MordomoProto.Fisica
{
    // Seguidor de trajetória (pure pursuit) em C# puro. Saída: (v, yawRate) desejados.
    // Mesma convenção do DifferentialDriveModel (yawRate > 0 = giro à direita).
    public sealed class PathFollower
    {
        readonly double[] _x, _z, _cum;
        int _seg;

        public double TotalLength { get; private set; }
        public double Lookahead = 0.35;
        public double MaxSpeed = 0.6;
        public double MaxYawRate = 55.0 * Math.PI / 180.0;
        public double Braking = 1.0;
        public double EffectiveTrack = 0.483;
        public double GoalTolerance = 0.03;
        public double SpinAngle = 70.0 * Math.PI / 180.0;   // acima disso: gira no lugar

        public double Progress { get; private set; }
        public double CrossTrack { get; private set; }
        public double TargetX { get; private set; }
        public double TargetZ { get; private set; }
        public bool Done { get; private set; }
        public double GoalX { get { return _x[_x.Length - 1]; } }
        public double GoalZ { get { return _z[_z.Length - 1]; } }

        public PathFollower(double[] xs, double[] zs)
        {
            if (xs == null || zs == null || xs.Length != zs.Length || xs.Length < 2)
                throw new ArgumentException("Trajetória precisa de ao menos 2 pontos.");
            _x = (double[])xs.Clone();
            _z = (double[])zs.Clone();
            _cum = new double[_x.Length];
            for (int i = 1; i < _x.Length; i++)
            {
                double dx = _x[i] - _x[i - 1], dz = _z[i] - _z[i - 1];
                _cum[i] = _cum[i - 1] + Math.Sqrt(dx * dx + dz * dz);
            }
            TotalLength = _cum[_cum.Length - 1];
        }

        void Locate(double px, double pz)
        {
            double best = double.MaxValue;
            int bestSeg = _seg;
            double bestS = Progress;
            int last = Math.Min(_x.Length - 2, _seg + 12);
            for (int i = _seg; i <= last; i++)
            {
                double ax = _x[i], az = _z[i];
                double dx = _x[i + 1] - ax, dz = _z[i + 1] - az;
                double l2 = dx * dx + dz * dz;
                double t = l2 > 1e-12 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0.0;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = ax + t * dx, qz = az + t * dz;
                double d = Math.Sqrt((px - qx) * (px - qx) + (pz - qz) * (pz - qz));
                if (d < best - 1e-9)
                {
                    best = d; bestSeg = i; bestS = _cum[i] + t * Math.Sqrt(l2);
                }
            }
            _seg = bestSeg;
            if (bestS > Progress) Progress = bestS;
            CrossTrack = best;
        }

        void PointAt(double s, out double x, out double z)
        {
            if (s >= TotalLength) { x = _x[_x.Length - 1]; z = _z[_z.Length - 1]; return; }
            int i = _seg;
            while (i < _x.Length - 2 && _cum[i + 1] < s) i++;
            double segLen = _cum[i + 1] - _cum[i];
            double t = segLen > 1e-12 ? (s - _cum[i]) / segLen : 0.0;
            x = _x[i] + t * (_x[i + 1] - _x[i]);
            z = _z[i] + t * (_z[i + 1] - _z[i]);
        }

        // Pose (posição e tangente) da trajetória a 's' metros à frente do ponto mais próximo.
        public void PoseAhead(double s, out double x, out double z, out double yaw)
        {
            double target = Math.Min(TotalLength, Progress + s);
            int i = _seg;
            while (i < _x.Length - 2 && _cum[i + 1] < target) i++;
            double segLen = _cum[i + 1] - _cum[i];
            double t = segLen > 1e-12 ? (target - _cum[i]) / segLen : 0.0;
            x = _x[i] + t * (_x[i + 1] - _x[i]);
            z = _z[i] + t * (_z[i + 1] - _z[i]);
            yaw = Math.Atan2(_x[i + 1] - _x[i], _z[i + 1] - _z[i]);
        }

        public void Update(Pose2D pose, double speed, out double v, out double yawRate)
        {
            Locate(pose.X, pose.Z);
            double remaining = TotalLength - Progress;
            double tx, tz;
            PointAt(Math.Min(TotalLength, Progress + Lookahead), out tx, out tz);
            TargetX = tx; TargetZ = tz;

            if (Done || (remaining < GoalTolerance && Math.Abs(speed) < 0.02))
            {
                Done = true; v = 0; yawRate = 0; return;
            }

            double dx = tx - pose.X, dz = tz - pose.Z;
            double fx = Math.Sin(pose.Yaw), fz = Math.Cos(pose.Yaw);
            double rx = Math.Cos(pose.Yaw), rz = -Math.Sin(pose.Yaw);
            double lf = dx * fx + dz * fz;
            double lr = dx * rx + dz * rz;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            double alpha = Math.Atan2(lr, lf);

            if (Math.Abs(alpha) > SpinAngle && remaining > GoalTolerance)
            {
                double w = alpha * 2.0;
                double lim = MaxYawRate;
                if (w > lim) w = lim; else if (w < -lim) w = -lim;
                if (Math.Abs(w) < 0.2) w = 0.2 * Math.Sign(w);
                v = 0; yawRate = w; return;
            }

            double kappa = dist > 1e-6 ? 2.0 * lr / (dist * dist) : 0.0;
            double ak = Math.Abs(kappa);
            double vmax = MaxSpeed / (1.0 + ak * EffectiveTrack * 0.5);   // roda externa <= MaxSpeed
            if (ak > 1e-6) vmax = Math.Min(vmax, MaxYawRate / ak);
            double stopDist = Math.Max(0.0, remaining - 0.01);
            vmax = Math.Min(vmax, Math.Sqrt(2.0 * Braking * 0.8 * stopDist));
            vmax *= Math.Max(0.15, Math.Cos(Math.Min(Math.Abs(alpha), 1.2)));
            v = vmax;
            yawRate = v * kappa;
        }
    }
}
