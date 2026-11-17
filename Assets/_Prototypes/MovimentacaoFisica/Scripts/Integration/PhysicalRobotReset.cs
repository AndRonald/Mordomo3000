using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace MordomoProto.Fisica
{
    // Reset do robô da VARIANTE física (tecla R). Não existe reset do robô no projeto original (o R original só limpa encomendas).
    // Restaura pose inicial e zera o estado do corpo, do adaptador, do agente, do controlador legado e do sensor.
    // Estado privado do controlador/sensor é limpo por reflexão (os originais não são alterados). Cada passo é registrado em LastReport.
    [DisallowMultipleComponent]
    public sealed class PhysicalRobotReset : MonoBehaviour
    {
        public bool keyEnabled = true;
        public Vector3 initialPosition; public float initialYawDeg; public bool captured;
        public string LastReport = "";
        public int ResetCount;

        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        void Start()
        {
            if (!captured) { initialPosition = transform.position; initialYawDeg = transform.eulerAngles.y; captured = true; }
        }

        void Update()
        {
            if (keyEnabled && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetNow();
        }

        static bool SetField(object o, string name, object v, StringBuilder log)
        {
            var f = o.GetType().GetField(name, BF);
            if (f == null) { log.AppendLine("campo ausente: " + o.GetType().Name + "." + name); return false; }
            f.SetValue(o, v); return true;
        }

        public string ResetNow()
        {
            var log = new StringBuilder();
            if (!captured) { initialPosition = transform.position; initialYawDeg = transform.eulerAngles.y; captured = true; }
            var ctl = GetComponent<RobotNavigationController>();
            var sensor = GetComponent<RobotSensorSuite>();
            var ad = GetComponent<PhysicalDriveAdapter>();
            var agent = GetComponent<NavMeshAgent>();

            // 1) controlador legado: cancela destino/recuperação/estado (privados -> reflexão)
            if (ctl != null)
            {
                SetField(ctl, "hasActiveDestination", false, log);
                SetField(ctl, "currentForwardSpeed", 0f, log);
                SetField(ctl, "recoveryLoggedAlignedBlocked", false, log);
                SetField(ctl, "recoveryLoggedPivotMismatch", false, log);
                SetField(ctl, "recoveryHasRoute", false, log);
                SetField(ctl, "hasLoggedRepathFailure", false, log);
                var rr = ctl.GetType().GetMethod("ResetRecoveryProgress", BF);
                if (rr != null) rr.Invoke(ctl, null); else log.AppendLine("método ausente: ResetRecoveryProgress");
                var sn = ctl.GetType().GetMethod("SetNavigationState", BF);
                if (sn != null) sn.Invoke(ctl, new object[] { RobotNavigationState.Idle }); else log.AppendLine("método ausente: SetNavigationState");
            }
            // 2) sensor: esquece a detecção anterior
            if (sensor != null)
            {
                SetField(sensor, "detectedObstacle", null, log);
                SetField(sensor, "nextScanTime", 0f, log);
            }
            // 3) corpo + adaptador + agente
            if (ad != null) ad.HardReset(initialPosition, initialYawDeg);
            else if (agent != null) agent.Warp(initialPosition);
            ResetCount++;
            log.AppendLine("reset #" + ResetCount + " para " + initialPosition.ToString("F2") + " yaw=" + initialYawDeg.ToString("F1"));
            LastReport = log.ToString();
            return LastReport;
        }
    }
}
