using System;

namespace MordomoProto.Fisica
{
    // Modelo diferencial cinemático em C# PURO (sem UnityEngine).
    //
    // Convenção (idêntica à do Unity): plano XZ, yaw em radianos em torno de +Y,
    // frente = (sin yaw, cos yaw). Yaw crescente = giro para a DIREITA (horário visto de cima).
    // yawRate > 0  =>  roda esquerda mais rápida que a direita.
    //
    // χ (Chi) multiplica a bitola geométrica: bitola efetiva = χ·b. Em skid-steer real o
    // escorregamento lateral faz χ > 1. Aqui χ é apenas um PARÂMETRO; nenhum valor foi calibrado
    // contra um robô real.
    //   - Chi            : χ da "planta" (como o robô realmente gira).
    //   - ControllerChi  : χ assumido pelo controlador ao converter (v, yawRate) em velocidades de roda.
    public sealed class DriveParameters
    {
        public double Track = 0.483;          // bitola geométrica (m)
        public double WheelRadius = 0.14;     // raio da roda (m)
        public double Chi = 1.0;
        public double ControllerChi = 1.0;
        public double MaxSpeed = 0.6;         // m/s
        public double MaxWheelSpeed = 0.6;    // m/s por lado
        public double Acceleration = 0.7;     // m/s² por roda (aumentando |v|)
        public double Braking = 1.0;          // m/s² por roda (reduzindo |v|)
        public double MaxYawRate = 55.0 * Math.PI / 180.0;

        public double EffectiveTrack { get { return Track * Chi; } }

        static bool Ok(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        // Fase S: parâmetros fisicamente impossíveis (bitola/raio/χ <= 0, limites <= 0, NaN, infinito) são rejeitados.
        public bool IsValid(out string reason)
        {
            reason = null;
            if (!Ok(Track) || Track <= 0.0) reason = "Track";
            else if (!Ok(WheelRadius) || WheelRadius <= 0.0) reason = "WheelRadius";
            else if (!Ok(Chi) || Chi <= 0.0) reason = "Chi";
            else if (!Ok(ControllerChi) || ControllerChi <= 0.0) reason = "ControllerChi";
            else if (!Ok(MaxSpeed) || MaxSpeed <= 0.0) reason = "MaxSpeed";
            else if (!Ok(MaxWheelSpeed) || MaxWheelSpeed <= 0.0) reason = "MaxWheelSpeed";
            else if (!Ok(Acceleration) || Acceleration <= 0.0) reason = "Acceleration";
            else if (!Ok(Braking) || Braking <= 0.0) reason = "Braking";
            else if (!Ok(MaxYawRate) || MaxYawRate <= 0.0) reason = "MaxYawRate";
            return reason == null;
        }
        public DriveParameters Clone() { return (DriveParameters)MemberwiseClone(); }
    }

    public struct Pose2D
    {
        public double X, Z, Yaw;
        public Pose2D(double x, double z, double yaw) { X = x; Z = z; Yaw = yaw; }
    }

    public struct StepPlan
    {
        public double Left, Right;     // velocidades de roda ao final do passo
        public double V, YawRate;      // movimento do corpo (planta, com χ)
        public double Dt;
        public Pose2D End;             // pose ao final do passo se nada bloquear
    }

    public sealed class DifferentialDriveModel
    {
        public DriveParameters Parameters { get; set; }
        public Pose2D Pose { get; private set; }
        public double LeftSpeed { get; private set; }
        public double RightSpeed { get; private set; }
        public double LeftAngle { get; private set; }    // rad acumulados
        public double RightAngle { get; private set; }
        public double UnwrappedYaw { get; private set; } // rad acumulados (sem wrap)

        public DifferentialDriveModel(DriveParameters p) { Parameters = p; }

        public double LinearSpeed { get { return 0.5 * (LeftSpeed + RightSpeed); } }
        public double YawRate
        {
            get { return (LeftSpeed - RightSpeed) / (Parameters.Chi * Parameters.Track); }
        }

        // ---- cinemática (estática, pura) ----
        public static void InverseKinematics(double v, double yawRate, double track, double chi,
                                             out double left, out double right)
        {
            double b = track * chi;
            left = v + 0.5 * yawRate * b;
            right = v - 0.5 * yawRate * b;
        }

        public static void ForwardKinematics(double left, double right, double track, double chi,
                                             out double v, out double yawRate)
        {
            v = 0.5 * (left + right);
            yawRate = (left - right) / (track * chi);
        }

        public static double WrapAngle(double a)
        {
            return a - 2.0 * Math.PI * Math.Floor((a + Math.PI) / (2.0 * Math.PI));
        }

        // Integração exata em arco de curvatura constante.
        public static Pose2D Integrate(Pose2D p, double v, double yawRate, double dt)
        {
            double yaw1 = p.Yaw + yawRate * dt;
            double x, z;
            if (Math.Abs(yawRate) < 1e-9)
            {
                x = p.X + v * dt * Math.Sin(p.Yaw);
                z = p.Z + v * dt * Math.Cos(p.Yaw);
            }
            else
            {
                double r = v / yawRate;
                x = p.X + r * (Math.Cos(p.Yaw) - Math.Cos(yaw1));
                z = p.Z + r * (Math.Sin(yaw1) - Math.Sin(p.Yaw));
            }
            return new Pose2D(x, z, WrapAngle(yaw1));
        }

        // NaN vira 0 (parada segura). Antes, NaN passava pelos dois testes e corrompia a pose para sempre.
        static double Clamp(double v, double lo, double hi) { if (double.IsNaN(v)) return 0.0; return v < lo ? lo : (v > hi ? hi : v); }
        static bool Finito(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        // Limitador de aceleração por roda.
        public static double Slew(double current, double target, double accel, double brake, double dt)
        {
            double delta = target - current;
            bool decel = Math.Abs(target) < Math.Abs(current) || target * current < 0.0;
            double maxStep = (decel ? brake : accel) * dt;
            return current + Clamp(delta, -maxStep, maxStep);
        }

        // Calcula o passo SEM alterar o estado (permite validar colisão antes de confirmar).
        public StepPlan Plan(double targetV, double targetYawRate, double dt)
        {
            var p = Parameters;
            if (!(dt > 0.0) || double.IsInfinity(dt))   // dt inválido (NaN, <= 0, infinito): passo nulo
            {
                var nop = new StepPlan();
                nop.Left = LeftSpeed; nop.Right = RightSpeed; nop.V = 0.0; nop.YawRate = 0.0; nop.Dt = 0.0; nop.End = Pose;
                return nop;
            }
            targetV = Clamp(targetV, -p.MaxSpeed, p.MaxSpeed);
            targetYawRate = Clamp(targetYawRate, -p.MaxYawRate, p.MaxYawRate);

            double tl, tr;
            InverseKinematics(targetV, targetYawRate, p.Track, p.ControllerChi, out tl, out tr);
            double m = Math.Max(Math.Abs(tl), Math.Abs(tr));
            if (m > p.MaxWheelSpeed && m > 1e-12)
            {
                double s = p.MaxWheelSpeed / m; // preserva a curvatura
                tl *= s; tr *= s;
            }

            var plan = new StepPlan();
            plan.Dt = dt;
            plan.Left = Slew(LeftSpeed, tl, p.Acceleration, p.Braking, dt);
            plan.Right = Slew(RightSpeed, tr, p.Acceleration, p.Braking, dt);
            ForwardKinematics(plan.Left, plan.Right, p.Track, p.Chi, out plan.V, out plan.YawRate);
            plan.End = Integrate(Pose, plan.V, plan.YawRate, dt);
            return plan;
        }

        // Confirma o passo. fraction < 1 => movimento bloqueado: avança só a fração segura e para as rodas.
        public int RejectedCommits { get; private set; }

        public void Commit(StepPlan plan, double fraction)
        {
            if (!Finito(plan.Left) || !Finito(plan.Right) || !Finito(plan.V) || !Finito(plan.YawRate) || !Finito(plan.Dt) ||
                !Finito(plan.End.X) || !Finito(plan.End.Z) || !Finito(plan.End.Yaw))
            {
                RejectedCommits++;            // plano não finito: mantém a pose, para as rodas
                LeftSpeed = 0.0; RightSpeed = 0.0;
                return;
            }
            double f = Clamp(fraction, 0.0, 1.0);
            Pose = Integrate(Pose, plan.V, plan.YawRate, plan.Dt * f);
            UnwrappedYaw += plan.YawRate * plan.Dt * f;
            double r = Parameters.WheelRadius;
            LeftAngle += plan.Left / r * plan.Dt * f;
            RightAngle += plan.Right / r * plan.Dt * f;
            if (f >= 1.0) { LeftSpeed = plan.Left; RightSpeed = plan.Right; }
            else { LeftSpeed = 0.0; RightSpeed = 0.0; }
        }

        // Passo completo (sem colisão) — útil em testes matemáticos.
        public void Step(double targetV, double targetYawRate, double dt)
        {
            Commit(Plan(targetV, targetYawRate, dt), 1.0);
        }

        public void SetPose(Pose2D p) { Pose = p; }

        public void ResetMotion()
        {
            LeftSpeed = 0; RightSpeed = 0; LeftAngle = 0; RightAngle = 0; UnwrappedYaw = 0;
        }
    }
}
