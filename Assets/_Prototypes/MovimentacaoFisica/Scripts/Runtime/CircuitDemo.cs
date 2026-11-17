using UnityEngine;

namespace MordomoProto.Fisica
{
    // Demonstração dentro da cena PrototipoFisica_Circuito: reinicia o robô no início do circuito e executa o cenário.
    public sealed class CircuitDemo : MonoBehaviour
    {
        public enum Scenario { Percurso, Giro360 }

        public KinematicDriveBody body;
        public CircuitRunner runner;
        public Scenario scenario = Scenario.Percurso;
        public Vector3 circuitOrigin = Vector3.zero;
        public float corridorWidth = 1.20f;
        public bool withObstacle = true;
        public bool autoStart = true;

        void Start()
        {
            if (autoStart) Run();
        }

        public void Run()
        {
            var info = CircuitBuilder.Build(null, circuitOrigin, corridorWidth, withObstacle, null, null, null, null, false);
            runner.body = body;
            runner.Zones.Clear();
            runner.Zones.Add(new System.Collections.Generic.KeyValuePair<string, Bounds>("porta", info.DoorZone));
            runner.Zones.Add(new System.Collections.Generic.KeyValuePair<string, Bounds>("curva", info.CurveZone));
            runner.BypassPlanner = from => CircuitBuilder.BuildBypass(info, from);
            if (scenario == Scenario.Giro360)
            {
                body.Teleport(new Vector3(info.RoomCenter.x, 0f, info.RoomCenter.y), 0f);
                runner.BeginSpin(360f);
            }
            else
            {
                body.Teleport(info.StartPosition, info.StartYawDeg);
                runner.BeginPath(info.Path, true, withObstacle);
            }
        }
    }
}
