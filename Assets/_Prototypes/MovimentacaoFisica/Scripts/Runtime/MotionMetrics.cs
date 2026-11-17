using System.Collections.Generic;
using System.Text;

namespace MordomoProto.Fisica
{
    public sealed class MotionMetrics
    {
        public bool Reached;
        public string FailReason = "";
        public double Duration;
        public double Distance;
        public double PeakSpeed;
        public double MinClearance = double.PositiveInfinity;   // m (perímetro da pegada -> obstáculo)
        public double MaxPenetration;                           // m (ComputePenetration)
        public double CteMax, CteSumSq; public long CteCount;  // erro de trajetória (m)
        public int ContactStops;
        public int Recoveries;
        public double FinalGoalError = double.NaN;              // m
        public double StopGapFront = double.NaN;                // m (para-choque -> obstáculo ao parar)
        public double SpinYawErrorDeg = double.NaN;
        public double SpinDrift = double.NaN;                   // m
        public double AvgStepMicros, MaxStepMicros;
        public readonly Dictionary<string, double> ZoneMinClearance = new Dictionary<string, double>();

        public double CteRms { get { return CteCount > 0 ? System.Math.Sqrt(CteSumSq / CteCount) : 0.0; } }

        public string Summary(string name)
        {
            var sb = new StringBuilder();
            sb.Append(name).Append(" | reached=").Append(Reached);
            if (!Reached && FailReason.Length > 0) sb.Append(" (").Append(FailReason).Append(")");
            sb.AppendFormat(" t={0:F2}s dist={1:F2}m vpeak={2:F3}", Duration, Distance, PeakSpeed);
            sb.AppendFormat(" | folgaMin={0:F4}m penetMax={1:F5}m", MinClearance, MaxPenetration);
            sb.AppendFormat(" | cteMax={0:F4}m cteRms={1:F4}m goalErr={2:F4}m", CteMax, CteRms, FinalGoalError);
            sb.AppendFormat(" | contactStops={0} recoveries={1}", ContactStops, Recoveries);
            if (!double.IsNaN(StopGapFront)) sb.AppendFormat(" stopGap={0:F3}m", StopGapFront);
            if (!double.IsNaN(SpinYawErrorDeg)) sb.AppendFormat(" spinYawErr={0:F3}deg spinDrift={1:F5}m", SpinYawErrorDeg, SpinDrift);
            foreach (var kv in ZoneMinClearance) sb.AppendFormat(" zone[{0}]={1:F4}m", kv.Key, kv.Value);
            sb.AppendFormat(" | step={0:F1}us(max {1:F1}us)", AvgStepMicros, MaxStepMicros);
            return sb.ToString();
        }
    }
}
