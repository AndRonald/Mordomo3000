using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    // Fase A: PhysicalDriveAdapter no circuito ISOLADO (NavMesh em tempo de execução). Nada da Bairro é usado.
    // Velocidade real = deslocamento da POSE FÍSICA por passo (não agent.velocity).
    public class PhysicalDriveAdapterTests
    {
        const float SpeedLimit = 0.60f, SpeedTol = 0.005f;
        const float YawLimitDeg = 55f, YawTolDeg = 0.5f;
        const float MaxPen = 0.001f;
        const float JumpLimit = SpeedLimit * 0.02f + 0.002f;       // deslocamento máx. por passo de 20 ms
        const float RotLimitDeg = YawLimitDeg * 0.02f + 0.1f;      // rotação máx. por passo

        sealed class Rig
        {
            public GameObject Root; public CircuitInfo Info; public KinematicDriveBody Body;
            public NavMeshAgent Agent; public PhysicalDriveAdapter Adapter; public NavMeshDataInstance Handle;
            public FootprintProbe Probe;
            public void Destroy()
            {
                if (Handle.valid) NavMesh.RemoveNavMeshData(Handle);
                if (Root != null) UnityEngine.Object.Destroy(Root);
            }
        }

        sealed class Track
        {
            public double MaxPen, MinClear = double.PositiveInfinity, CteMax, Time;
            public Vector3 Last; public float Dist;
        }

        // modo escolhido após medição (ver fase_a.txt): o agente fica parado e só planeja
        const AgentAdvanceMode DefaultMode = AgentAdvanceMode.StoppedAgent;

        readonly List<Rig> _live = new List<Rig>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var r in _live) r.Destroy();
            _live.Clear();
        }

        Rig Make(float width, AgentAdvanceMode mode, bool bakeOnly = false)
        {
            var r = new Rig();
            r.Root = new GameObject("AdapterRig_" + width.ToString("F2"));
            r.Info = CircuitBuilder.Build(r.Root.transform, Vector3.zero, width, false, null, null, null, "A_" + width.ToString("F2"));
            Physics.SyncTransforms();
            var sources = new List<NavMeshBuildSource>(); var markups = new List<NavMeshBuildMarkup>();
            var bounds = new Bounds(new Vector3(4f, 0f, 4f), new Vector3(24f, 10f, 24f));
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var settings = NavMesh.GetSettingsByIndex(0);   // mesmo Agent Type do projeto (raio 0,28 / altura 1,1)
            r.Handle = NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity));

            r.Body = RobotRig.CreateBare("Robo_Adaptador", r.Info.StartPosition, r.Info.StartYawDeg, new DriveConfig());
            r.Body.transform.SetParent(r.Root.transform, true);
            var go = r.Body.gameObject;
            r.Agent = go.AddComponent<NavMeshAgent>();     // parâmetros iguais aos do Robot_Cubo
            r.Agent.speed = 0.6f; r.Agent.acceleration = 0.7f; r.Agent.angularSpeed = 55f;
            r.Agent.radius = 0.28f; r.Agent.height = 1.1f;
            r.Agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            r.Agent.autoBraking = true;
            r.Agent.updatePosition = false; r.Agent.updateRotation = false;
            r.Agent.Warp(r.Info.StartPosition);
            r.Adapter = go.AddComponent<PhysicalDriveAdapter>();
            r.Adapter.mode = mode;
            r.Adapter.SetMode(mode);
            r.Probe = r.Body.guard.Probe;
            _live.Add(r);
            return r;
        }

        static float DistToPolyline(Vector3 p, List<Vector2> path)
        {
            float best = float.MaxValue;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i], q = new Vector2(p.x, p.z), ab = b - a;
                float l2 = ab.sqrMagnitude, t = l2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / l2) : 0f;
                best = Mathf.Min(best, (q - (a + t * ab)).magnitude);
            }
            return best;
        }

        static void Sample(Rig r, Track tr, float dt, bool clearance = true)
        {
            Vector3 p = r.Body.Position; Quaternion q = r.Body.Rotation;
            tr.Time += dt;
            tr.Dist += new Vector2(p.x - tr.Last.x, p.z - tr.Last.z).magnitude; tr.Last = p;
            double pen = r.Probe.MeasurePenetration(p, q); if (pen > tr.MaxPen) tr.MaxPen = pen;
            if (clearance) { double c = r.Probe.MeasureClearance(p, q); if (c < tr.MinClear) tr.MinClear = c; }
            double cte = DistToPolyline(p, r.Info.Path); if (cte > tr.CteMax) tr.CteMax = cte;
        }

        static string Fmt(string name, Rig r, Track tr, string extra = "")
        {
            var a = r.Adapter;
            return string.Format("{0} | estado={1} t={2:F2}s dist={3:F2}m | velRealMax={4:F4} yawMax={5:F2}°/s accelMax={6:F3} | passoMaxDesloc={7:F4}m passoMaxRot={8:F3}° | " +
                "diverg(pré-sync) média={9:F5} máx={10:F5} | diverg(pós-sync)máx={11:F6} | penetMax={12:F5} folgaMin={13:F4} cteMax={14:F4} | custo adaptador={15:F1}us(máx {16:F1}) corpo={17:F1}us(máx {18:F1}) contactStops={19} {20}",
                name, a.State, tr.Time, tr.Dist, a.MaxRealSpeed, a.MaxYawRateDegPerSec, a.MaxLinearAccel, a.MaxStepDisplacement, a.MaxStepRotationDeg,
                a.MeanDivergence, a.MaxDivergence, a.MaxPostSyncDivergence, tr.MaxPen, double.IsInfinity(tr.MinClear) ? -1 : tr.MinClear, tr.CteMax,
                a.MeanAdapterMicros, a.MaxAdapterMicros, r.Body.AverageStepMicroseconds, r.Body.MaxStepMicroseconds, r.Body.ContactStopCount, extra);
        }

        static void AssertCommon(Rig r, Track tr, bool pen = true)
        {
            var a = r.Adapter;
            Assert.LessOrEqual(a.MaxRealSpeed, SpeedLimit + SpeedTol, "velocidade real > 0,60 m/s");
            Assert.LessOrEqual(a.MaxYawRateDegPerSec, YawLimitDeg + YawTolDeg, "giro > 55°/s");
            Assert.LessOrEqual(a.MaxStepDisplacement, JumpLimit, "salto de pose por passo");
            Assert.LessOrEqual(a.MaxStepRotationDeg, RotLimitDeg, "salto de rotação por passo");
            Assert.LessOrEqual(a.MaxPostSyncDivergence, 0.001, "agente não ficou sincronizado com a pose física");
            Assert.Less(a.MeanAdapterMicros + r.Body.AverageStepMicroseconds, 1000.0, "custo médio por passo >= 1 ms");
            if (pen) Assert.LessOrEqual(tr.MaxPen, MaxPen, "penetração");
        }

        IEnumerator RunUntil(Rig r, Track tr, float timeout, Func<bool> done, Action<float> each = null)
        {
            float t = 0f;
            while (t < timeout)
            {
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime; t += dt;
                Sample(r, tr, dt);
                if (each != null) each(t);
                if (done != null && done()) yield break;
            }
        }

        static Vector3 V(Vector2 v) { return new Vector3(v.x, 0f, v.y); }

        // ---- 1/3/4/11/12: reta, aceleração, frenagem, sem avanço duplicado — comparando os 3 modos do agente ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Reta_Aceleracao_Frenagem_TresModos()
        {
            foreach (var mode in new[] { AgentAdvanceMode.OverwritePose, AgentAdvanceMode.FreezeSpeed, AgentAdvanceMode.StoppedAgent })
            {
                var r = Make(1.20f, mode);
                yield return null; yield return null;
                var tr = new Track { Last = r.Body.Position };
                Vector3 dest = r.Info.StartPosition + Vector3.forward * 2.6f;
                Assert.IsTrue(r.Adapter.SetDestination(dest), "sem rota");
                double tReach06 = -1; double peak = 0; double vPrev = 0; bool braked = false; double brakeStartSpeed = 0, brakeDist = 0; Vector3 brakePos = default(Vector3);
                yield return RunUntil(r, tr, 25f, () => r.Adapter.State == AdapterState.Arrived, t =>
                {
                    double v = r.Body.LinearSpeed; if (v > peak) peak = v;
                    if (tReach06 < 0 && v >= 0.595) tReach06 = t;
                });
                float goalErr = Horizontal(r.Body.Position, dest);
                // pose física x agente depois de chegar
                float agentErr = Horizontal(r.Agent.nextPosition, r.Body.Position);
                string line = Fmt("RETA modo=" + mode, r, tr, string.Format("| chegou={0} erroFinal={1:F4}m pico={2:F3} t(0,6)={3:F2}s agente-corpo={4:F5}", r.Adapter.State == AdapterState.Arrived, goalErr, peak, tReach06, agentErr));
                PlayResultsLog.Append("fase_a.txt", line);
                if (mode == DefaultMode)
                {
                    Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
                    Assert.LessOrEqual(goalErr, 0.06f);
                    AssertCommon(r, tr);
                    Assert.That(tReach06, Is.InRange(0.75, 1.1), "aceleração incoerente com 0,7 m/s²");
                }
                r.Destroy(); _live.Remove(r);
                yield return null;
            }
        }

        static float Horizontal(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

        // ---- 2: curvas (corredor 1,20 m, curva 90° + porta) via rota do NavMesh ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Curva_Porta_Corredor120()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            Vector3 dest = V(r.Info.Goal);
            Assert.IsTrue(r.Adapter.SetDestination(dest));
            yield return RunUntil(r, tr, 60f, () => r.Adapter.State == AdapterState.Arrived);
            float goalErr = Horizontal(r.Body.Position, dest);
            PlayResultsLog.Append("fase_a.txt", Fmt("CURVA+PORTA L=1,20", r, tr, string.Format("| chegou={0} erroFinal={1:F4}m", r.Adapter.State == AdapterState.Arrived, goalErr)));
            Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
            AssertCommon(r, tr);
        }

        // curva + porta nos três modos do agente (decide como impedir o avanço independente do agente)
        [UnityTest, Timeout(300000)]
        public IEnumerator Curva_Porta_TresModos()
        {
            foreach (var mode in new[] { AgentAdvanceMode.OverwritePose, AgentAdvanceMode.FreezeSpeed })
            {
                var r = Make(1.20f, mode);
                yield return null; yield return null;
                var tr = new Track { Last = r.Body.Position };
                Vector3 dest = V(r.Info.Goal);
                bool ok = r.Adapter.SetDestination(dest);
                yield return RunUntil(r, tr, 60f, () => r.Adapter.State == AdapterState.Arrived || r.Adapter.State == AdapterState.Blocked);
                PlayResultsLog.Append("fase_a.txt", Fmt("CURVA+PORTA L=1,20 modo=" + mode, r, tr, string.Format("| rota={0} chegou={1} erroFinal={2:F4}m", ok, r.Adapter.State == AdapterState.Arrived, Horizontal(r.Body.Position, dest))));
                AssertCommon(r, tr);
                r.Destroy(); _live.Remove(r);
                yield return null;
            }
        }

        // informativo: corredor restrito 0,90 m (o controlador atual não chegou aqui)
        [UnityTest, Timeout(300000)]
        public IEnumerator Curva_Porta_Corredor090_Informativo()
        {
            var r = Make(0.90f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            Vector3 dest = V(r.Info.Goal);
            bool ok = r.Adapter.SetDestination(dest);
            yield return RunUntil(r, tr, 60f, () => r.Adapter.State == AdapterState.Arrived || r.Adapter.State == AdapterState.Blocked);
            float goalErr = Horizontal(r.Body.Position, dest);
            PlayResultsLog.Append("fase_a.txt", Fmt("CURVA+PORTA L=0,90 (informativo)", r, tr, string.Format("| rota={0} chegou={1} erroFinal={2:F4}m", ok, r.Adapter.State == AdapterState.Arrived, goalErr)));
            // critérios de segurança valem mesmo se não chegar
            AssertCommon(r, tr);
        }

        // ---- 5: mudança de destino durante o movimento ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Mudanca_De_Destino_Reversao_Informativo()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            r.Adapter.SetDestination(V(r.Info.Goal));
            bool changed = false; Vector3 newDest = r.Info.StartPosition + Vector3.forward * 0.4f;
            var trace = new StringBuilder(); float nextTr = 0f;
            yield return RunUntil(r, tr, 40f, () => changed && r.Adapter.State == AdapterState.Arrived, t =>
            {
                if (!changed && t > 3f) { changed = true; r.Adapter.SetDestination(newDest); }
                if (t >= nextTr) { nextTr += 0.5f; trace.AppendFormat("[{0:F1}s ({1:F2},{2:F2}) yaw{3:F0} v{4:F2} {5}] ", t, r.Body.Position.x, r.Body.Position.z, r.Body.Pose.Yaw * Mathf.Rad2Deg, r.Body.LinearSpeed, r.Adapter.State); }
            });
            PlayResultsLog.Append("fase_a_trace.txt", trace.ToString());
            float goalErr = Horizontal(r.Body.Position, newDest);
            PlayResultsLog.Append("fase_a.txt", Fmt("MUDANÇA DE DESTINO c/ REVERSÃO 180° em corredor (informativo: giro exige 0,4257 m de raio varrido)", r, tr, string.Format("| estado final={0} erroFinal={1:F4}m", r.Adapter.State, goalErr)));
            // critério: parada bloqueada sem penetrar OU chegada; nunca penetração, salto ou violação de limites
            Assert.That(r.Adapter.State, Is.EqualTo(AdapterState.Arrived).Or.EqualTo(AdapterState.Blocked));
            AssertCommon(r, tr);
        }

        // ---- 5: mudança de destino durante o movimento, sem reversão ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Mudanca_De_Destino_Em_Movimento()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            r.Adapter.SetDestination(r.Info.StartPosition + Vector3.forward * 2.6f);
            bool changed = false; float vAtChange = 0f; Vector3 goal = V(r.Info.Goal);
            yield return RunUntil(r, tr, 60f, () => changed && r.Adapter.State == AdapterState.Arrived, t =>
            {
                if (!changed && t > 2f) { changed = true; vAtChange = r.Body.LinearSpeed; r.Adapter.SetDestination(goal); }
            });
            float errA = Horizontal(r.Body.Position, goal);
            PlayResultsLog.Append("fase_a.txt", Fmt("MUDANÇA DE DESTINO (a) reta->curva+porta", r, tr, string.Format("| v no instante da troca={0:F3} chegou={1} erroFinal={2:F4}m", vAtChange, r.Adapter.State == AdapterState.Arrived, errA)));
            Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
            Assert.LessOrEqual(errA, 0.06f);
            AssertCommon(r, tr);
            r.Destroy(); _live.Remove(r);
            yield return null;
            r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            tr = new Track { Last = r.Body.Position };
            r.Adapter.SetDestination(V(r.Info.Goal));
            changed = false; Vector3 near = r.Info.StartPosition + Vector3.forward * 2.2f;
            yield return RunUntil(r, tr, 40f, () => changed && r.Adapter.State == AdapterState.Arrived, t =>
            {
                if (!changed && t > 3f) { changed = true; vAtChange = r.Body.LinearSpeed; r.Adapter.SetDestination(near); }
            });
            float errB = Horizontal(r.Body.Position, near);
            PlayResultsLog.Append("fase_a.txt", Fmt("MUDANÇA DE DESTINO (b) longa->ponto próximo", r, tr, string.Format("| v na troca={0:F3} chegou={1} erroFinal={2:F4}m", vAtChange, r.Adapter.State == AdapterState.Arrived, errB)));
            Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
            Assert.LessOrEqual(errB, 0.06f);
            AssertCommon(r, tr);
        }

        // ---- 6/7: parada e retomada ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Parada_E_Retomada()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            Vector3 dest = V(r.Info.Goal);
            r.Adapter.SetDestination(dest);
            yield return RunUntil(r, tr, 3f, null);
            double vBefore = r.Body.LinearSpeed;
            r.Adapter.Stop();
            yield return RunUntil(r, tr, 1.2f, null);
            Vector3 stopPos = r.Body.Position; double vAfterBrake = r.Body.LinearSpeed;
            yield return RunUntil(r, tr, 2f, null);
            float drift = Horizontal(r.Body.Position, stopPos);
            Assert.AreEqual(AdapterState.Idle, r.Adapter.State);
            Assert.LessOrEqual(drift, 0.001f, "moveu depois de parar");
            Assert.Less(vAfterBrake, 0.01);
            Assert.IsTrue(r.Adapter.SetDestination(dest), "retomada sem rota");
            yield return RunUntil(r, tr, 60f, () => r.Adapter.State == AdapterState.Arrived);
            float goalErr = Horizontal(r.Body.Position, dest);
            PlayResultsLog.Append("fase_a.txt", Fmt("PARADA+RETOMADA", r, tr, string.Format("| vAntes={0:F3} vApós1,2s={1:F4} deriva2s={2:F5}m chegou={3} erroFinal={4:F4}m", vBefore, vAfterBrake, drift, r.Adapter.State == AdapterState.Arrived, goalErr)));
            Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
            AssertCommon(r, tr);
        }

        // ---- 8: pausa (lógica e Time.timeScale = 0) ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Pausa_Logica_E_TimeScale()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            Vector3 dest = V(r.Info.Goal);
            r.Adapter.SetDestination(dest);
            yield return RunUntil(r, tr, 3f, null);
            // pausa lógica
            r.Adapter.SetPaused(true);
            yield return RunUntil(r, tr, 1.2f, null);
            Vector3 pp = r.Body.Position; Vector3 ap = r.Agent.nextPosition;
            yield return RunUntil(r, tr, 2f, null);
            float driftLogic = Horizontal(r.Body.Position, pp); float agentDrift = Horizontal(r.Agent.nextPosition, ap);
            // pausa da simulação
            Vector3 pb = r.Body.Position; Vector3 pa = r.Agent.nextPosition; Quaternion rotB = r.Body.Rotation;
            Time.timeScale = 0f;
            for (int i = 0; i < 30; i++) yield return null;
            float driftScale = Horizontal(r.Body.Position, pb); float driftScaleAgent = Horizontal(r.Agent.nextPosition, pa);
            Time.timeScale = 1f;
            r.Adapter.SetPaused(false);
            yield return RunUntil(r, tr, 60f, () => r.Adapter.State == AdapterState.Arrived);
            float goalErr = Horizontal(r.Body.Position, dest);
            PlayResultsLog.Append("fase_a.txt", Fmt("PAUSA", r, tr, string.Format("| deriva lógica 2s={0:F5}m (agente {1:F5}) | timeScale0: deriva corpo={2:F6} agente={3:F6} | retomou chegou={4} erroFinal={5:F4}m", driftLogic, agentDrift, driftScale, driftScaleAgent, r.Adapter.State == AdapterState.Arrived, goalErr)));
            Assert.LessOrEqual(driftLogic, 0.001f, "moveu durante pausa lógica");
            Assert.LessOrEqual(driftScale, 1e-5f, "moveu com timeScale 0");
            Assert.AreEqual(AdapterState.Arrived, r.Adapter.State);
            AssertCommon(r, tr);
        }

        // ---- 9: caminho bloqueado (obstáculo físico que o NavMesh não conhece) ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Caminho_Bloqueado_ParadaSegura()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(r.Root.transform, false);
            wall.transform.position = new Vector3(0f, 0.5f, 1.8f); wall.transform.localScale = new Vector3(1.2f, 1.0f, 0.2f);   // fecha o corredor
            Physics.SyncTransforms();
            var tr = new Track { Last = r.Body.Position };
            Vector3 dest = r.Info.StartPosition + Vector3.forward * 2.6f;
            r.Adapter.SetDestination(dest);
            yield return RunUntil(r, tr, 20f, () => r.Adapter.State == AdapterState.Blocked);
            Vector3 p = r.Body.Position; yield return RunUntil(r, tr, 2f, null);
            float drift = Horizontal(r.Body.Position, p);
            PlayResultsLog.Append("fase_a.txt", Fmt("CAMINHO BLOQUEADO", r, tr, string.Format("| deriva após Blocked={0:F5}m zFinal={1:F4} (face do obstáculo z=1,70; frente do robô z+0,325={2:F4})", drift, r.Body.Position.z, r.Body.Position.z + 0.325f)));
            Assert.AreEqual(AdapterState.Blocked, r.Adapter.State);
            Assert.LessOrEqual(drift, 0.001f);
            Assert.LessOrEqual(tr.MaxPen, MaxPen);
            AssertCommon(r, tr);
        }

        // ---- 9b: destino sem rota ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Destino_Sem_Rota_Nao_Move()
        {
            var r = Make(1.20f, DefaultMode);
            yield return null; yield return null;
            var tr = new Track { Last = r.Body.Position };
            Vector3 p0 = r.Body.Position;
            bool ok = r.Adapter.SetDestination(new Vector3(40f, 0f, 40f));
            yield return RunUntil(r, tr, 3f, null);
            float moved = Horizontal(r.Body.Position, p0);
            PlayResultsLog.Append("fase_a.txt", Fmt("SEM ROTA", r, tr, string.Format("| SetDestination={0} movimento={1:F5}m", ok, moved)));
            Assert.AreEqual(AdapterState.NoPath, r.Adapter.State);
            Assert.LessOrEqual(moved, 0.001f);
        }
    }
}
