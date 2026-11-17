using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    public static class PlayResultsLog
    {
        public static void Append(string file, string text)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ProtoResults");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, file), text + "\n");
        }
    }

    sealed class Harness
    {
        public GameObject Root;
        public CircuitInfo Info;
        public KinematicDriveBody Body;
        public CircuitRunner Runner;

        public static Harness Create(float width, bool obstacle, DriveConfig cfg = null)
        {
            var h = new Harness();
            h.Root = new GameObject("TestHarness_" + width.ToString("F2"));
            h.Info = CircuitBuilder.Build(h.Root.transform, Vector3.zero, width, obstacle, null, null, null, "Teste_" + width.ToString("F2"));
            Physics.SyncTransforms();
            h.Body = RobotRig.CreateBare("Robo_Teste", h.Info.StartPosition, h.Info.StartYawDeg, cfg ?? new DriveConfig());
            h.Body.transform.SetParent(h.Root.transform, true);
            h.Runner = h.Body.gameObject.AddComponent<CircuitRunner>();
            h.Runner.body = h.Body;
            var info = h.Info;
            h.Runner.BypassPlanner = from => CircuitBuilder.BuildBypass(info, from);
            h.Runner.Zones.Add(new KeyValuePair<string, Bounds>("porta", info.DoorZone));
            h.Runner.Zones.Add(new KeyValuePair<string, Bounds>("curva", info.CurveZone));
            return h;
        }

        public void Destroy() { if (Root != null) UnityEngine.Object.Destroy(Root); }
    }

    public class CircuitScenarioTests
    {
        const float MinClearanceRequired = 0.02f;     // 2 x collisionSkin atual (0,01 m); critério único, não reduzido
        const float MaxPenetration = 0.001f;           // 1 mm
        const float MaxCte = 0.10f;
        const float GoalTolerance = 0.05f;
        const float TimeScale = 8f;

        readonly List<Harness> _live = new List<Harness>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var h in _live) h.Destroy();
            _live.Clear();
        }

        Harness Make(float width, bool obstacle, DriveConfig cfg = null)
        {
            var h = Harness.Create(width, obstacle, cfg);
            _live.Add(h);
            return h;
        }

        static IEnumerator RunToEnd(CircuitRunner r)
        {
            Time.timeScale = TimeScale;
            while (!r.Finished) yield return new WaitForFixedUpdate();
            Time.timeScale = 1f;
        }

        static void Log(string name, MotionMetrics m)
        {
            string s = m.Summary(name);
            Debug.Log("[PROTO] " + s);
            PlayResultsLog.Append("cenarios.txt", s);
        }

        static void AssertSafe(MotionMetrics m, bool requireClearance = true)
        {
            Assert.LessOrEqual(m.MaxPenetration, MaxPenetration, "penetração máxima (m)");
            if (requireClearance) Assert.GreaterOrEqual(m.MinClearance, MinClearanceRequired, "folga mínima (m)");
        }

        [UnityTest]
        public IEnumerator Aceleracao_E_Frenagem_NaSala()
        {
            var h = Make(1.20f, false);
            var body = h.Body;
            body.Teleport(new Vector3(h.Info.RoomCenter.x, 0f, h.Info.RoomCenter.y - 1.0f), 0f);
            Time.timeScale = TimeScale;
            float z0 = body.Position.z, t0 = Time.fixedTime;
            body.SetCommand(0.6f, 0f);
            while (body.LinearSpeed < 0.6f - 1e-4f && Time.fixedTime - t0 < 3f) yield return new WaitForFixedUpdate();
            float tAccel = Time.fixedTime - t0;
            float dAccel = body.Position.z - z0;
            Assert.AreEqual(0.6f / 0.7f, tAccel, 0.06f, "tempo de aceleração");
            Assert.AreEqual(body.Model.LeftSpeed, body.Model.RightSpeed, 1e-9, "rodas iguais em linha reta");
            // mantém 0,3 s e freia
            float tHold = Time.fixedTime;
            while (Time.fixedTime - tHold < 0.3f) yield return new WaitForFixedUpdate();
            float zb = body.Position.z, minV = body.LinearSpeed;
            body.SetCommand(0f, 0f);
            for (int i = 0; i < 100 && Mathf.Abs(body.LinearSpeed) > 1e-6f; i++)
            {
                yield return new WaitForFixedUpdate();
                minV = Mathf.Min(minV, body.LinearSpeed);
            }
            float stopDist = body.Position.z - zb;
            Time.timeScale = 1f;
            string s = string.Format("Aceleracao/Frenagem | tAccel={0:F3}s dAccel={1:F3}m | distFrenagem={2:F3}m (teórico 0,180) | minV={3:F4} | contactStops={4} | passoMedio={5:F1}us",
                tAccel, dAccel, stopDist, minV, body.ContactStopCount, body.AverageStepMicroseconds);
            Debug.Log("[PROTO] " + s); PlayResultsLog.Append("cenarios.txt", s);
            Assert.AreEqual(0.18f, stopDist, 0.015f, "distância de frenagem");
            Assert.GreaterOrEqual(minV, -1e-4f, "sem marcha à ré");
            Assert.AreEqual(0, body.ContactStopCount);
        }

        [UnityTest]
        public IEnumerator Giro360_NaSala()
        {
            var h = Make(1.20f, false);
            h.Body.Teleport(new Vector3(h.Info.RoomCenter.x, 0f, h.Info.RoomCenter.y), 0f);
            h.Runner.BeginSpin(360f);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Giro360 sala", m);
            Assert.IsTrue(m.Reached);
            Assert.AreEqual(0.0, m.SpinYawErrorDeg, 0.5);
            Assert.Less(m.SpinDrift, 0.01);
            AssertSafe(m);
        }

        [UnityTest]
        public IEnumerator Giro360_CorredorRestrito_090_Informativo()
        {
            var h = Make(0.90f, false);
            h.Body.Teleport(new Vector3(0f, 0f, 1.5f), 0f);
            h.Runner.BeginSpin(360f);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Giro360 corredor 0,90 (varredura 0,851 m)", m);
            Assert.IsTrue(m.Reached);
            Assert.AreEqual(0.0, m.SpinYawErrorDeg, 0.5);
            AssertSafe(m, false);
            Assert.Greater(m.MinClearance, 0f);
        }

        [UnityTest]
        public IEnumerator Giro360_CorredorEstreito_080_BloqueadoPelaVarredura()
        {
            var h = Make(0.80f, false);       // menor que o diâmetro varrido (0,851 m)
            h.Body.Teleport(new Vector3(0f, 0f, 1.5f), 0f);
            h.Runner.maxDuration = 20f;
            h.Runner.BeginSpin(360f);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Giro360 corredor 0,80 (deve ser bloqueado)", m);
            Assert.IsFalse(m.Reached, "não pode completar o giro");
            Assert.GreaterOrEqual(m.ContactStops, 1);
            Assert.LessOrEqual(m.MaxPenetration, MaxPenetration);
        }

        [UnityTest]
        public IEnumerator Circuito_Principal_120_ComPorta()
        {
            var h = Make(1.20f, false);
            h.Runner.BeginPath(h.Info.Path, true, false);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Principal 1,20 (curva+porta)", m);
            Assert.IsTrue(m.Reached, m.FailReason);
            Assert.LessOrEqual(m.FinalGoalError, GoalTolerance);
            Assert.LessOrEqual(m.CteMax, MaxCte);
            Assert.AreEqual(0, m.ContactStops);
            AssertSafe(m);
        }

        [UnityTest]
        public IEnumerator Circuito_Restrito_090_ComCurva()
        {
            var h = Make(0.90f, false);
            h.Runner.BeginPath(h.Info.Path, true, false);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Restrito 0,90 (curva+porta)", m);
            Assert.IsTrue(m.Reached, m.FailReason);
            Assert.LessOrEqual(m.FinalGoalError, GoalTolerance);
            Assert.LessOrEqual(m.CteMax, MaxCte);
            Assert.AreEqual(0, m.ContactStops);
            AssertSafe(m);
        }

        [UnityTest]
        public IEnumerator Obstaculo_Frenagem_Recuperacao_Principal()
        {
            var h = Make(1.20f, true);
            h.Runner.BeginPath(h.Info.Path, true, true);
            yield return RunToEnd(h.Runner);
            var m = h.Runner.Metrics; Log("Obstaculo+recuperacao 1,20", m);
            Assert.IsTrue(m.Reached, m.FailReason);
            Assert.AreEqual(1, m.Recoveries, "uma recuperação");
            Assert.AreEqual(0, m.ContactStops, "o sensor/frenagem evita o contato");
            Assert.GreaterOrEqual(m.StopGapFront, h.Runner.stopMargin - 0.06f);
            Assert.LessOrEqual(m.FinalGoalError, GoalTolerance);
            AssertSafe(m);
        }

        [UnityTest]
        public IEnumerator Guarda_ColisaoForcada_SemPenetracao()
        {
            var h = Make(1.20f, false);
            // de frente para a parede interna (x = +0,6): sem sensor, o guarda de varredura deve parar o corpo
            h.Body.Teleport(new Vector3(0f, 0f, 1.0f), 90f);
            Time.timeScale = TimeScale;
            h.Body.SetCommand(0.6f, 0f);
            float t0 = Time.fixedTime; float maxPen = 0f;
            var probe = h.Body.guard.Probe;
            while (Time.fixedTime - t0 < 3.0f)
            {
                yield return new WaitForFixedUpdate();
                maxPen = Mathf.Max(maxPen, probe.MeasurePenetration(h.Body.Position, h.Body.Rotation));
            }
            Time.timeScale = 1f;
            float gap = probe.MeasureClearance(h.Body.Position, h.Body.Rotation);
            string s = string.Format("Colisao forcada | x_final={0:F4}m (contato em 0,2750) | folga={1:F4}m | penetracaoMax={2:F5}m | contactStops={3}",
                h.Body.Position.x, gap, maxPen, h.Body.ContactStopCount);
            Debug.Log("[PROTO] " + s); PlayResultsLog.Append("cenarios.txt", s);
            Assert.GreaterOrEqual(h.Body.ContactStopCount, 1);
            Assert.LessOrEqual(maxPen, MaxPenetration);
            Assert.AreEqual(0.275f, h.Body.Position.x, 0.004f);
        }

        [UnityTest]
        public IEnumerator Sensibilidade_Chi_NoCircuito()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Sensibilidade a χ no circuito com física (guarda de varredura ativo) ==");
            foreach (float width in new[] { 1.20f, 0.90f })
                foreach (float chi in new[] { 1.0f, 1.2f, 1.5f })
                    foreach (bool comp in new[] { false, true })
                    {
                        var cfg = new DriveConfig { chi = chi, controllerChi = comp ? chi : 1f };
                        var h = Make(width, false, cfg);
                        h.Runner.BeginPath(h.Info.Path, true, false);
                        yield return RunToEnd(h.Runner);
                        var m = h.Runner.Metrics;
                        sb.AppendLine(m.Summary("L=" + width.ToString("F2") + " chi=" + chi.ToString("F1") + (comp ? " compensado" : " SEM compensar")));
                        Assert.LessOrEqual(m.MaxPenetration, MaxPenetration, "penetração com χ=" + chi);
                        if (comp) { Assert.IsTrue(m.Reached, m.FailReason); Assert.GreaterOrEqual(m.MinClearance, MinClearanceRequired); }
                        h.Destroy(); _live.Remove(h);
                        yield return null;
                    }
            Debug.Log("[PROTO] " + sb);
            PlayResultsLog.Append("sensibilidade_chi_circuito.txt", sb.ToString());
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator Rodas_Prefab_SincronizamComCadaLado()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prototypes/MovimentacaoFisica/Prefabs/Robo_Prototipo.prefab");
            Assert.IsNotNull(prefab);
            var h = Make(1.20f, false);
            var go = UnityEngine.Object.Instantiate(prefab);
            var body = go.GetComponent<KinematicDriveBody>();
            var sync = go.GetComponent<WheelVisualSync>();
            body.Teleport(new Vector3(h.Info.RoomCenter.x, 0f, h.Info.RoomCenter.y - 1.0f), 0f);
            Time.timeScale = 4f;
            body.SetCommand(0.5f, 0f);
            float t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 1.5f) yield return new WaitForFixedUpdate();
            body.SetCommand(0.25f, 0.5f);
            t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 2.0f) yield return new WaitForFixedUpdate();
            Time.timeScale = 1f;
            yield return null;   // deixa o LateUpdate rodar
            var m = body.Model;
            // (esq - dir)·r = yaw·bitola  (cinemática)
            Assert.AreEqual(m.UnwrappedYaw * 0.483, (m.LeftAngle - m.RightAngle) * 0.14, 1e-3);
            Assert.Greater(m.LeftAngle, m.RightAngle);
            var fl = go.transform.Find("Rodas/Pivo_Roda_FE"); var rl = go.transform.Find("Rodas/Pivo_Roda_TE");
            var fr = go.transform.Find("Rodas/Pivo_Roda_FD"); var rr = go.transform.Find("Rodas/Pivo_Roda_TD");
            Assert.Less(Quaternion.Angle(fl.localRotation, rl.localRotation), 0.01f, "lado esquerdo gira junto");
            Assert.Less(Quaternion.Angle(fr.localRotation, rr.localRotation), 0.01f, "lado direito gira junto");
            Assert.Less(Quaternion.Angle(fl.localRotation, Quaternion.AngleAxis(sync.LastLeftAngleDeg, Vector3.right)), 0.01f);
            Assert.Less(Quaternion.Angle(fr.localRotation, Quaternion.AngleAxis(sync.LastRightAngleDeg, Vector3.right)), 0.01f);
            Assert.Greater(Quaternion.Angle(fl.localRotation, fr.localRotation), 1f, "lados com ângulos diferentes na curva");
            // eixo de rotação é X local
            Vector3 axis; float ang; fl.localRotation.ToAngleAxis(out ang, out axis);
            Assert.AreEqual(1f, Mathf.Abs(axis.x), 1e-3f);
            string s = string.Format("Rodas | angEsq={0:F1}° angDir={1:F1}° (acum. rad esq={2:F2} dir={3:F2}) | pivôs FE==TE e FD==TD OK", sync.LastLeftAngleDeg, sync.LastRightAngleDeg, m.LeftAngle, m.RightAngle);
            Debug.Log("[PROTO] " + s); PlayResultsLog.Append("cenarios.txt", s);
            UnityEngine.Object.Destroy(go);
        }

        // ---------- comparação com o controlador ATUAL (NavMesh + Robot_Cubo; somente leitura do prefab) ----------
        sealed class LegacyResult
        {
            public bool Arrived; public float Time, Distance, MinClearance = float.PositiveInfinity, MaxPenetration, CteMax, GoalError; public int Recoveries;
            public float PeakSpeed, PeakActualSpeed, MeanActualSpeed, MovingTime;
            public Vector3 FinalPos; public string State; public bool HasPath; public int Corners; public float PathLen; public string Trace = "";
        }

        IEnumerator RunLegacy(float width, LegacyResult res)
        {
            var root = new GameObject("LegacyHarness_" + width.ToString("F2"));
            var info = CircuitBuilder.Build(root.transform, Vector3.zero, width, false, null, null, null, "Legado_" + width.ToString("F2"));
            Physics.SyncTransforms();

            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            var bounds = new Bounds(new Vector3(4f, 0f, 4f), new Vector3(24f, 10f, 24f));
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var settings = NavMesh.GetSettingsByIndex(0);   // Agent Type atual do projeto (raio 0,28 / altura 1,1)
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            var handle = NavMesh.AddNavMeshData(data);
            yield return null;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Robot_Cubo.prefab");
            var robot = UnityEngine.Object.Instantiate(prefab, info.StartPosition, Quaternion.identity);
            var ctl = robot.GetComponent<RobotNavigationController>();
            ctl.SetKeyboardShortcutsEnabled(false);
            bool arrived = false;
            ctl.Arrived += () => arrived = true;
            yield return null; yield return null;
            var agent = robot.GetComponent<NavMeshAgent>();
            if (!agent.isOnNavMesh) agent.Warp(info.StartPosition);

            var probe = new FootprintProbe(robot.GetComponent<BoxCollider>(), robot.transform);
            Vector3 dest = new Vector3(info.Goal.x, 0f, info.Goal.y);
            res.HasPath = ctl.HasCompletePathTo(dest);
            var navPath = new NavMeshPath();
            NavMesh.CalculatePath(info.StartPosition, dest, NavMesh.AllAreas, navPath);
            res.Corners = navPath.corners.Length;
            for (int ci = 0; ci < navPath.corners.Length - 1; ci++) res.PathLen += Vector3.Distance(navPath.corners[ci], navPath.corners[ci + 1]);
            bool ok = ctl.MoveTo(dest);
            float nextTrace = 0f;
            float t0 = UnityEngine.Time.time;
            Vector3 last = robot.transform.position;
            var refPath = info.Path;
            while (!arrived && UnityEngine.Time.time - t0 < 90f)
            {
                yield return null;
                Vector3 p = robot.transform.position; Quaternion q = robot.transform.rotation;
                float disp = (p - last).magnitude; res.Distance += disp; last = p;
                float dtf = UnityEngine.Time.deltaTime;
                if (dtf > 1e-4f) { float sp = disp / dtf; if (sp > res.PeakActualSpeed) res.PeakActualSpeed = sp; if (sp > 0.05f) res.MovingTime += dtf; }
                if (UnityEngine.Time.time - t0 >= nextTrace) { res.Trace += string.Format("[{0:F0}s ({1:F2},{2:F2}) yaw{3:F0}] ", UnityEngine.Time.time - t0, p.x, p.z, q.eulerAngles.y); nextTrace += 5f; }
                float c = probe.MeasureClearance(p, q); if (c < res.MinClearance) res.MinClearance = c;
                float pen = probe.MeasurePenetration(p, q); if (pen > res.MaxPenetration) res.MaxPenetration = pen;
                double best = double.MaxValue;
                for (int i = 0; i < refPath.Count - 1; i++)
                {
                    Vector2 a = refPath[i], b = refPath[i + 1]; Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
                    float t = l2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(new Vector2(p.x, p.z) - a, ab) / l2) : 0f;
                    double d = Vector2.Distance(a + ab * t, new Vector2(p.x, p.z)); if (d < best) best = d;
                }
                if (best > res.CteMax) res.CteMax = (float)best;
                if (agent.velocity.magnitude > res.PeakSpeed) res.PeakSpeed = agent.velocity.magnitude;
            }
            res.Arrived = arrived && ok;
            res.Time = UnityEngine.Time.time - t0;
            Vector3 pf = robot.transform.position;
            res.GoalError = Vector2.Distance(new Vector2(pf.x, pf.z), info.Goal);
            res.Recoveries = ctl.RecoveryRotationCount;
            res.State = ctl.CurrentNavigationState.ToString();
            res.FinalPos = pf;
            res.MeanActualSpeed = res.Time > 0f ? res.Distance / res.Time : 0f;
            UnityEngine.Object.Destroy(robot);
            NavMesh.RemoveNavMeshData(handle);
            UnityEngine.Object.Destroy(root);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Comparacao_ControladorAtual_vs_Prototipo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Comparação (mesmo circuito, mesmo collider 0,55 x 1,10 x 0,65) ==");
            foreach (float width in new[] { 1.20f, 0.90f })
            {
                var lr = new LegacyResult();
                yield return RunLegacy(width, lr);
                sb.AppendFormat("ATUAL  L={0:F2} | chegou={1} t={2:F1}s dist={3:F2}m vReal(pico/média)={4:F2}/{5:F2} m/s (agent.velocity pico {10:F2}) | folgaMin={6:F4}m penetMax={7:F4}m | cteMax(vs linha de centro)={8:F3}m goalErr={9:F3}m recup={11} estado={12} caminhoNavMesh={13} cantos={14} compr={15:F2}m\n   trilha: {16}\n",
                    width, lr.Arrived, lr.Time, lr.Distance, lr.PeakActualSpeed, lr.MeanActualSpeed, lr.MinClearance, lr.MaxPenetration, lr.CteMax, lr.GoalError, lr.PeakSpeed, lr.Recoveries, lr.State, lr.HasPath, lr.Corners, lr.PathLen, lr.Trace);

                var h = Make(width, false);
                h.Runner.BeginPath(h.Info.Path, true, false);
                yield return RunToEnd(h.Runner);
                var m = h.Runner.Metrics;
                sb.AppendFormat("PROTÓT L={0:F2} | chegou={1} t={2:F1}s dist={3:F2}m vpico={4:F2} | folgaMin={5:F4}m penetMax={6:F4}m | cteMax={7:F3}m goalErr={8:F3}m\n",
                    width, m.Reached, m.Duration, m.Distance, m.PeakSpeed, m.MinClearance, m.MaxPenetration, m.CteMax, m.FinalGoalError);
                h.Destroy(); _live.Remove(h);
                yield return null;
            }
            Debug.Log("[PROTO] " + sb);
            PlayResultsLog.Append("comparacao.txt", sb.ToString());
            Assert.Pass(sb.ToString());
        }
#endif
    }
}
