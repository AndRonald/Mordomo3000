using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace MordomoProto.Fisica.Tests
{
    public static class EditResultsLog
    {
        public static void Append(string file, string text)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ProtoResults");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, file), text + "\n");
        }
    }

    public class DifferentialDriveModelTests
    {
        const double Dt = 0.02;
        static readonly double[] ChiValues = { 1.0, 1.1, 1.2, 1.3, 1.5, 1.8, 2.0 };

        static DifferentialDriveModel NewModel(double chi = 1.0, double controllerChi = 1.0)
        {
            return new DifferentialDriveModel(new DriveParameters { Chi = chi, ControllerChi = controllerChi });
        }

        [Test]
        public void Kinematics_RoundTrip()
        {
            foreach (double chi in new[] { 1.0, 1.4, 2.0 })
                foreach (double v in new[] { -0.4, 0.0, 0.3, 0.6 })
                    foreach (double w in new[] { -0.9, 0.0, 0.5 })
                    {
                        double l, r, v2, w2;
                        DifferentialDriveModel.InverseKinematics(v, w, 0.483, chi, out l, out r);
                        DifferentialDriveModel.ForwardKinematics(l, r, 0.483, chi, out v2, out w2);
                        Assert.AreEqual(v, v2, 1e-12);
                        Assert.AreEqual(w, w2, 1e-12);
                    }
        }

        [Test]
        public void Straight_BothWheelsEqual_NoYaw()
        {
            double l, r;
            DifferentialDriveModel.InverseKinematics(0.6, 0.0, 0.483, 1.0, out l, out r);
            Assert.AreEqual(0.6, l, 1e-12);
            Assert.AreEqual(0.6, r, 1e-12);
        }

        [Test]
        public void SpinInPlace_WheelSpeedsMatchTrack()
        {
            double w = 55.0 * Math.PI / 180.0; // 0,96 rad/s
            double l, r;
            DifferentialDriveModel.InverseKinematics(0.0, w, 0.483, 1.0, out l, out r);
            Assert.AreEqual(0.2318, l, 1e-3);   // ≈ 0,232 m/s por lado (cf. relatório da Etapa 1)
            Assert.AreEqual(-l, r, 1e-12);
            DifferentialDriveModel.InverseKinematics(0.0, w, 0.483, 1.5, out l, out r);
            Assert.AreEqual(0.2318 * 1.5, l, 2e-3); // χ escala a velocidade de roda
        }

        [Test]
        public void YawConvention_RightTurnIncreasesYaw_ForwardIsSinCos()
        {
            // yawRate > 0 => roda esquerda mais rápida => yaw cresce (giro à direita)
            var m = NewModel();
            for (int i = 0; i < 50; i++) m.Step(0.0, 0.5, Dt);
            Assert.Greater(m.Pose.Yaw, 0.0);
            Assert.Greater(m.LeftSpeed, m.RightSpeed);
            // frente com yaw = 90° aponta para +X
            var p = DifferentialDriveModel.Integrate(new Pose2D(0, 0, Math.PI * 0.5), 1.0, 0.0, 1.0);
            Assert.AreEqual(1.0, p.X, 1e-12);
            Assert.AreEqual(0.0, p.Z, 1e-9);
        }

        [Test]
        public void Integrate_ConstantArc_ReturnsToStartAfterFullCircle()
        {
            double v = 0.4, w = 0.8;
            var p = new Pose2D(1.0, 2.0, 0.3);
            double T = 2.0 * Math.PI / w;
            int n = 500;
            for (int i = 0; i < n; i++) p = DifferentialDriveModel.Integrate(p, v, w, T / n);
            Assert.AreEqual(1.0, p.X, 1e-9);
            Assert.AreEqual(2.0, p.Z, 1e-9);
            Assert.AreEqual(0.3, DifferentialDriveModel.WrapAngle(p.Yaw), 1e-9);
        }

        [Test]
        public void Integrate_MatchesFineEuler()
        {
            var a = new Pose2D(0, 0, 0.2);
            var b = a;
            double v = 0.5, w = -0.7, T = 1.0;
            a = DifferentialDriveModel.Integrate(a, v, w, T);
            int n = 100000;
            for (int i = 0; i < n; i++)
                b = new Pose2D(b.X + v * Math.Sin(b.Yaw) * T / n, b.Z + v * Math.Cos(b.Yaw) * T / n, b.Yaw + w * T / n);
            Assert.AreEqual(a.X, b.X, 1e-4);
            Assert.AreEqual(a.Z, b.Z, 1e-4);
        }

        [Test]
        public void Acceleration_ReachesMaxSpeedInExpectedTime()
        {
            var m = NewModel();
            double t = 0, dist = 0;
            while (m.LinearSpeed < 0.6 - 1e-9 && t < 5.0) { m.Step(0.6, 0.0, Dt); t += Dt; }
            Assert.AreEqual(0.6 / 0.7, t, 2 * Dt);                   // 0,857 s
            Assert.AreEqual(0.6, m.LinearSpeed, 1e-9);
            Assert.AreEqual(m.LeftSpeed, m.RightSpeed, 1e-12);
            Assert.AreEqual(0.0, m.Pose.X, 1e-9);                    // sem desvio lateral
            dist = m.Pose.Z;
            Assert.AreEqual(0.6 * 0.6 / (2 * 0.7), dist, 0.02);      // 0,257 m
        }

        [Test]
        public void Braking_StopsInExpectedDistance_NoReverse()
        {
            var m = NewModel();
            for (int i = 0; i < 100; i++) m.Step(0.6, 0.0, Dt);
            double z0 = m.Pose.Z, minV = m.LinearSpeed;
            for (int i = 0; i < 100; i++) { m.Step(0.0, 0.0, Dt); minV = Math.Min(minV, m.LinearSpeed); }
            Assert.AreEqual(0.0, m.LinearSpeed, 1e-12);
            Assert.GreaterOrEqual(minV, -1e-12);
            Assert.AreEqual(0.18, m.Pose.Z - z0, 0.012);              // 0,6²/(2·1,0)
        }

        [Test]
        public void WheelLimit_PreservesCurvature()
        {
            var m = NewModel();
            double v = 0.6, w = 55.0 * Math.PI / 180.0;
            double l0, r0;
            DifferentialDriveModel.InverseKinematics(v, w, 0.483, 1.0, out l0, out r0);
            Assert.Greater(Math.Max(l0, r0), 0.6); // excede o limite de roda
            for (int i = 0; i < 400; i++) m.Step(v, w, Dt);
            Assert.LessOrEqual(Math.Max(Math.Abs(m.LeftSpeed), Math.Abs(m.RightSpeed)), 0.6 + 1e-9);
            double curv0 = (l0 - r0) / (0.483 * 0.5 * (l0 + r0));
            double curv1 = (m.LeftSpeed - m.RightSpeed) / (0.483 * 0.5 * (m.LeftSpeed + m.RightSpeed));
            Assert.AreEqual(curv0, curv1, 1e-9);
        }

        [Test]
        public void WheelAngles_TrackDistanceOverRadius()
        {
            var m = NewModel();
            for (int i = 0; i < 300; i++) m.Step(0.5, 0.0, Dt);
            double dist = m.Pose.Z;
            Assert.AreEqual(dist / 0.14, m.LeftAngle, 1e-6);
            Assert.AreEqual(m.LeftAngle, m.RightAngle, 1e-9);
        }

        [Test]
        public void Commit_PartialFraction_StopsWheels()
        {
            var m = NewModel();
            for (int i = 0; i < 100; i++) m.Step(0.6, 0.0, Dt);
            var plan = m.Plan(0.6, 0.0, Dt);
            double z0 = m.Pose.Z;
            m.Commit(plan, 0.5);
            Assert.AreEqual(0.6 * Dt * 0.5, m.Pose.Z - z0, 1e-9);
            Assert.AreEqual(0.0, m.LinearSpeed, 1e-12);
        }

        [Test]
        public void Chi_Sensitivity_OpenLoopSpin_Table()
        {
            // Giro de 360° em malha aberta: o controlador calcula as rodas para "360°" com ControllerChi.
            var sb = new StringBuilder();
            sb.AppendLine("== Sensibilidade a χ: giro no lugar, malha aberta (comando = 360°) ==");
            sb.AppendLine("chi | yaw obtido SEM compensar (ControllerChi=1) | yaw obtido COMPENSADO (ControllerChi=χ) | deriva");
            double w0 = 55.0 * Math.PI / 180.0;
            double T = 2.0 * Math.PI / w0;
            double prev = double.MaxValue;
            foreach (double chi in ChiValues)
            {
                var a = NewModel(chi, 1.0);
                var b = NewModel(chi, chi);
                // ignora a rampa: aplica as rodas alvo diretamente (regime) para isolar o efeito de χ
                double drift = 0;
                int n = (int)Math.Round(T / Dt);
                for (int i = 0; i < n; i++) { a.Step(0.0, w0, Dt); b.Step(0.0, w0, Dt); }
                // tempo de rampa angular = w0 / (2·brake...) é pequeno; corrige comparando razão
                double yawA = Math.Abs(a.UnwrappedYaw) * 180 / Math.PI;
                double yawB = Math.Abs(b.UnwrappedYaw) * 180 / Math.PI;
                drift = Math.Sqrt(a.Pose.X * a.Pose.X + a.Pose.Z * a.Pose.Z);
                sb.AppendFormat("{0:F1} | {1:F2}° | {2:F2}° | {3:E1} m\n", chi, yawA, yawB, drift);
                Assert.AreEqual(0.0, drift, 1e-9);
                Assert.Less(yawA, prev + 1e-9, "yaw sem compensação deve diminuir com χ");
                prev = yawA;
                Assert.AreEqual(yawB, yawA * chi, yawB * 0.04, "razão yaw_compensado / yaw_sem = χ");
            }
            EditResultsLog.Append("sensibilidade_chi.txt", sb.ToString());
            UnityEngine.Debug.Log(sb.ToString());
        }

        static void MakePath(double width, out double[] xs, out double[] zs)
        {
            var px = new List<double>(); var pz = new List<double>();
            double R = width * 0.5, L1 = 3.0, L2 = 3.0, zc = L1 + R;
            for (double z = 0; z < zc - R; z += 0.05) { px.Add(0); pz.Add(z); }
            int n = Math.Max(8, (int)Math.Ceiling(R * Math.PI * 0.5 / 0.02));
            for (int i = 0; i <= n; i++)
            {
                double phi = Math.PI - (Math.PI * 0.5) * i / n;
                px.Add(R + R * Math.Cos(phi)); pz.Add(zc - R + R * Math.Sin(phi));
            }
            for (double x = R + 0.05; x < R + L2; x += 0.05) { px.Add(x); pz.Add(zc); }
            xs = px.ToArray(); zs = pz.ToArray();
        }

        static void RunCurve(double width, double chi, double controllerChi, out double cteMax, out double cteRms, out bool reached, out double time)
        {
            double[] xs, zs; MakePath(width, out xs, out zs);
            var f = new PathFollower(xs, zs);
            f.EffectiveTrack = 0.483 * controllerChi;
            var m = NewModel(chi, controllerChi);
            m.SetPose(new Pose2D(0, 0, 0));
            cteMax = 0; double ss = 0; int cnt = 0; reached = false; time = 0;
            for (int i = 0; i < 4000; i++)
            {
                double v, w;
                f.Update(m.Pose, m.LinearSpeed, out v, out w);
                if (f.Done) { reached = true; break; }
                m.Step(v, w, Dt);
                time += Dt;
                cteMax = Math.Max(cteMax, f.CrossTrack); ss += f.CrossTrack * f.CrossTrack; cnt++;
            }
            cteRms = cnt > 0 ? Math.Sqrt(ss / cnt) : 0;
        }

        [Test]
        public void Chi_Sensitivity_ClosedLoopCurve_Table()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Sensibilidade a χ: curva de 90° em malha fechada (seguidor puro, sem colisão) ==");
            sb.AppendLine("corredor | chi | sem compensar: cteMax / cteRms / tempo / chegou | compensado: cteMax / cteRms / tempo / chegou");
            foreach (double width in new[] { 1.20, 0.90 })
                foreach (double chi in ChiValues)
                {
                    double cm1, cr1, t1, cm2, cr2, t2; bool ok1, ok2;
                    RunCurve(width, chi, 1.0, out cm1, out cr1, out ok1, out t1);
                    RunCurve(width, chi, chi, out cm2, out cr2, out ok2, out t2);
                    sb.AppendFormat("{0:F2} | {1:F1} | {2:F3} / {3:F3} / {4:F1}s / {5} | {6:F3} / {7:F3} / {8:F1}s / {9}\n",
                        width, chi, cm1, cr1, t1, ok1, cm2, cr2, t2, ok2);
                    Assert.IsTrue(ok2, "compensado deve chegar (χ=" + chi + ")");
                    Assert.Less(cm2, 0.10, "compensado: erro de trajetória < 0,10 m (χ=" + chi + ", L=" + width + ")");
                    Assert.IsTrue(ok1, "sem compensar também deve chegar (malha fechada) χ=" + chi);
                }
            EditResultsLog.Append("sensibilidade_chi.txt", sb.ToString());
            UnityEngine.Debug.Log(sb.ToString());
        }

        [Test]
        public void PathFollower_StraightLine_StopsAtGoalWithoutOvershoot()
        {
            var xs = new List<double>(); var zs = new List<double>();
            for (double z = 0; z <= 3.0001; z += 0.05) { xs.Add(0); zs.Add(z); }
            var f = new PathFollower(xs.ToArray(), zs.ToArray());
            var m = NewModel();
            double maxZ = 0; bool done = false;
            for (int i = 0; i < 2000; i++)
            {
                double v, w; f.Update(m.Pose, m.LinearSpeed, out v, out w);
                if (f.Done) { done = true; break; }
                m.Step(v, w, Dt); maxZ = Math.Max(maxZ, m.Pose.Z);
            }
            Assert.IsTrue(done);
            Assert.AreEqual(3.0, m.Pose.Z, 0.03);
            Assert.LessOrEqual(maxZ, 3.0 + 0.01);
            Assert.AreEqual(0.0, m.Pose.X, 1e-6);
        }
    }
}
