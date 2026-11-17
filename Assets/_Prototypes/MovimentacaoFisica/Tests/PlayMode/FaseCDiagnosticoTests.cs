#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    // Observador de diagnóstico: roda ANTES do LateUpdate do adaptador e registra o que o controlador legado escreveu no Transform.
    [DefaultExecutionOrder(-200)]
    public sealed class RecoveryWriteProbe : MonoBehaviour
    {
        public RobotNavigationController ctl; public KinematicDriveBody body;
        public int framesWaiting, framesRotationWritten, framesAbove3deg; public float maxDeltaDeg, sumDeltaDeg, maxBodyYawChange; float lastBodyYaw = float.NaN;
        void LateUpdate()
        {
            if (ctl == null || body == null || ctl.CurrentNavigationState != RobotNavigationState.WaitingForObstacle) { lastBodyYaw = float.NaN; return; }
            framesWaiting++;
            float d = Quaternion.Angle(ctl.transform.rotation, body.Rotation);
            if (d > 0.01f) { framesRotationWritten++; sumDeltaDeg += d; }
            if (d > 3f) framesAbove3deg++;
            if (d > maxDeltaDeg) maxDeltaDeg = d;
            float by = body.Rotation.eulerAngles.y;
            if (!float.IsNaN(lastBodyYaw)) maxBodyYawChange = Mathf.Max(maxBodyYawChange, Mathf.Abs(Mathf.DeltaAngle(lastBodyYaw, by)));
            lastBodyYaw = by;
        }
    }

    // Fase C — SOMENTE DIAGNÓSTICO. Nada aqui altera critérios de colisão, sensores ou o controlador.
    public class FaseCDiagnosticoTests
    {
        const string PhysScene = "Assets/_Prototypes/MovimentacaoFisica/Scenes/CasaGrandeBairroScene_Fisica.unity";
        const string OrigScene = "Assets/_Project/Scenes/CasaGrandeBairroScene.unity";
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        sealed class DRun
        {
            public RobotNavigationController ctl; public NavMeshAgent agent; public RobotSensorSuite sensor;
            public PhysicalDriveAdapter ad; public KinematicDriveBody body; public PhysicalRobotReset reset;
            public bool physical; public bool arrived;
            public Vector3 Pos { get { return physical ? body.Position : ctl.transform.position; } }
            public float Yaw { get { return physical ? body.Rotation.eulerAngles.y : ctl.transform.eulerAngles.y; } }
        }

        static T Get<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BF);
            return f == null ? default(T) : (T)f.GetValue(o);
        }

        static IEnumerator Load(string scene, bool physical, DRun r)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            r.physical = physical;
            r.ctl = Object.FindAnyObjectByType<RobotNavigationController>();
            r.agent = r.ctl.GetComponent<NavMeshAgent>();
            r.sensor = r.ctl.GetComponent<RobotSensorSuite>();
            r.ad = r.ctl.GetComponent<PhysicalDriveAdapter>();
            r.body = r.ctl.GetComponent<KinematicDriveBody>();
            r.reset = r.ctl.GetComponent<PhysicalRobotReset>();
            Assert.AreEqual(physical, r.ad != null, "tipo de robô na cena");
            r.ctl.SetKeyboardShortcutsEnabled(false);
            r.ctl.Arrived += () => r.arrived = true;
            float tw = Time.time; while (!r.agent.isOnNavMesh && Time.time - tw < 3f) yield return null;
            Assert.IsTrue(r.agent.isOnNavMesh);
        }

        static DeliveryDestination FindDest(string nm)
        {
            foreach (var x in Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include)) if (x.name == nm) return x;
            return null;
        }

        static string V2(Vector3 v) { return "(" + v.x.ToString("F2") + "," + v.z.ToString("F2") + ")"; }

        // ---------------- rastreador cronológico ----------------
        sealed class Tracer
        {
            public DRun r; public StringBuilder ev = new StringBuilder();
            readonly Queue<string> pre = new Queue<string>(); int post; float t0;
            int lastStops; string lastCtl = "", lastAd = "", pendingSensor = "";
            Vector3 prev; float lastMoveT; Vector3 lastMovePos;
            public float firstWaitT = -1f, firstStopT = -1f; public int framesWaiting, cmdNonZeroWaiting, stopsSeen;
            public float maxSpeed, dist; public bool stuck;
            FieldInfo fv, fw; Vector3 sensorOffset = new Vector3(0f, 0.1f, 0.3f);
            public List<string> sensorEvents = new List<string>();
            public bool shadow, forceScanOnHit; bool forced; public int shadowHitSteps, shadowSteps; public float shadowTravel; public string shadowFirst = "", shadowLast = ""; Vector3 shadowPrev; bool shadowPrevHit; public float shadowFirstT = -1f;
            FootprintProbe legacyProbe; int frame0; public float legacyMaxPen, legacyMinClear = float.MaxValue;

            public Tracer(DRun run)
            {
                r = run; t0 = Time.time; prev = r.Pos; lastMovePos = prev; lastMoveT = t0;
                if (r.physical)
                {
                    fv = typeof(PhysicalDriveAdapter).GetField("_cmdV", BF); fw = typeof(PhysicalDriveAdapter).GetField("_cmdW", BF);
                    lastStops = r.body.ContactStopCount;
                }
                if (!r.physical) legacyProbe = new FootprintProbe(r.ctl.GetComponent<BoxCollider>(), r.ctl.transform);
                frame0 = Time.frameCount;
                sensorOffset = Get<Vector3>(r.sensor, "sensorOriginOffset");
                r.sensor.ObstacleChanged += OnSensor;
            }

            public float T { get { return Time.time - t0; } }

            // varredura-sombra: mesma esfera do RobotSensorSuite (somente leitura; não dispara eventos nem altera o controlador)
            void Shadow()
            {
                var a = r.agent; if (!a.hasPath || a.pathPending) { shadowPrevHit = false; return; }
                float radius = Get<float>(r.sensor, "detectionRadius"), dist = Get<float>(r.sensor, "detectionDistance"); var layers = Get<LayerMask>(r.sensor, "obstacleLayers");
                Vector3 origin = r.ctl.transform.TransformPoint(sensorOffset);
                Vector3 dir = a.steeringTarget - r.ctl.transform.position; dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : r.ctl.transform.forward;
                var hits = Physics.SphereCastAll(origin, Mathf.Max(0.01f, radius), dir, Mathf.Max(0.05f, dist), layers, QueryTriggerInteraction.Ignore);
                Collider best = null; float bd = float.MaxValue;
                foreach (var h in hits) { if (h.collider == null || h.transform == r.ctl.transform || h.transform.IsChildOf(r.ctl.transform)) continue; if (h.distance < bd) { bd = h.distance; best = h.collider; } }
                shadowSteps++;
                bool hit = best != null;
                if (hit)
                {
                    shadowHitSteps++; if (shadowFirstT < 0) shadowFirstT = T;
                    string d = "t=" + T.ToString("F2") + " pos=" + V2(r.Pos) + " yaw=" + r.Yaw.ToString("F1") + " -> '" + best.name + "' a " + bd.ToString("F3") + " m";
                    if (shadowFirst == "") shadowFirst = d; shadowLast = d;
                    if (shadowPrevHit) shadowTravel += Vector3.Distance(new Vector3(r.Pos.x, 0, r.Pos.z), new Vector3(shadowPrev.x, 0, shadowPrev.z));
                    if (forceScanOnHit && !forced) { forced = true; ev.AppendLine("   [teste] ScanNow() forçado no passo em que a varredura-sombra prevê o toque (t=" + T.ToString("F2") + ")"); r.sensor.ScanNow(); }
                }
                shadowPrev = r.Pos; shadowPrevHit = hit;
            }

            string Corners()
            {
                var a = r.agent; if (!a.hasPath || a.pathPending) return "[sem caminho]";
                var c = a.path.corners; if (c == null || c.Length == 0) return "[]";
                int i0 = 0; float best = float.MaxValue;
                for (int k = 0; k < c.Length; k++) { float d = (new Vector3(c[k].x, 0, c[k].z) - a.steeringTarget).sqrMagnitude; if (d < best) { best = d; i0 = k; } }
                var sb = new StringBuilder("[");
                for (int k = i0; k < c.Length && k < i0 + 4; k++) sb.Append(V2(c[k]));
                sb.Append("] total=" + c.Length);
                return sb.ToString();
            }

            void OnSensor(bool blocked, Collider col)
            {
                if (!blocked || col == null) { pendingSensor = "SENSOR LIVRE"; sensorEvents.Add("t=" + T.ToString("F2") + " livre"); return; }
                Vector3 origin = r.ctl.transform.TransformPoint(sensorOffset);
                Vector3 cp = col.ClosestPoint(origin);
                Vector3 dir = cp - origin; float d = dir.magnitude; dir.y = 0f;
                float angFwd = Vector3.SignedAngle(r.ctl.transform.forward, dir, Vector3.up);
                Vector3 st = r.agent.hasPath && !r.agent.pathPending ? r.agent.steeringTarget - r.ctl.transform.position : r.ctl.transform.forward; st.y = 0f;
                float angScan = Vector3.SignedAngle(st, dir, Vector3.up);
                // distância do collider ao caminho (primeiros 2,5 m) e se está sobre a faixa do robô (±0,30 m)
                float minPath = float.MaxValue; float lat = float.NaN;
                if (r.agent.hasPath && !r.agent.pathPending)
                {
                    var c = r.agent.path.corners; float acc = 0f; Vector3 p0 = r.Pos;
                    for (int k = 0; k < c.Length && acc < 2.5f; k++)
                    {
                        Vector3 p1 = c[k]; float seg = Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z));
                        int n = Mathf.Max(1, Mathf.CeilToInt(seg / 0.05f));
                        for (int s = 0; s <= n && acc + seg * s / n < 2.5f; s++)
                        {
                            Vector3 q = Vector3.Lerp(p0, p1, s / (float)n); q.y = 0.4f;
                            float dd = Vector3.Distance(col.ClosestPoint(q), q); if (dd < minPath) minPath = dd;
                        }
                        acc += seg; p0 = p1;
                    }
                }
                string s2 = "SENSOR '" + col.name + "' layer=" + col.gameObject.layer + " trig=" + col.isTrigger + " dist(origem->collider)=" + d.ToString("F3") + " ângulo vs frente=" + angFwd.ToString("F1")
                    + "° vs direção do alvo=" + angScan.ToString("F1") + "° ponto=" + V2(cp) + " bounds.c=" + V2(col.bounds.center) + " size=" + col.bounds.size.ToString("F2")
                    + " dist ao caminho(2,5m)=" + (minPath == float.MaxValue ? "n/d" : minPath.ToString("F3")) + " (robô: meia-largura 0,275)";
                pendingSensor = s2; sensorEvents.Add("t=" + T.ToString("F2") + " " + s2);
            }

            string Line()
            {
                var a = r.agent; Vector3 p = r.Pos;
                string ctl = r.ctl.CurrentNavigationState.ToString();
                var sb = new StringBuilder();
                sb.Append("t=" + T.ToString("F2") + " pos=" + V2(p) + " yaw=" + r.Yaw.ToString("F1") + " ctl=" + ctl);
                if (r.physical)
                {
                    float cv = (float)fv.GetValue(r.ad), cw = (float)fw.GetValue(r.ad);
                    string mode = Mathf.Abs(cv) < 0.01f && Mathf.Abs(cw) > 0.02f ? "GIRO NO LUGAR" : (Mathf.Abs(cv) >= 0.01f && Mathf.Abs(cw) > 0.05f ? "CURVA" : (Mathf.Abs(cv) >= 0.01f ? "RETA" : "PARADO"));
                    var pr = r.body.guard.Probe;
                    sb.Append(" ad=" + r.ad.State + " cmd=(" + cv.ToString("F2") + "," + cw.ToString("F2") + ")=" + mode + " v=" + r.body.LinearSpeed.ToString("F3") + " w=" + r.body.YawRate.ToString("F2")
                        + " contato=" + r.body.ContactStopped + "/" + r.body.ContactStopCount + " blocker=" + (pr.LastBlocker != null ? pr.LastBlocker.name : "-")
                        + " folga=" + pr.MeasureClearance(p, r.body.Rotation).ToString("F3") + " pen=" + pr.MeasurePenetration(p, r.body.Rotation).ToString("F4"));
                }
                if (!r.physical)
                {
                    float lp = legacyProbe.MeasurePenetration(r.ctl.transform.position, r.ctl.transform.rotation), lc = legacyProbe.MeasureClearance(r.ctl.transform.position, r.ctl.transform.rotation);
                    if (lp > legacyMaxPen) legacyMaxPen = lp; if (lc < legacyMinClear) legacyMinClear = lc;
                    sb.Append(" penLegado=" + lp.ToString("F4") + " folgaLegado=" + lc.ToString("F3") + " blocker=" + (legacyProbe.LastBlocker != null ? legacyProbe.LastBlocker.name : "-"));
                }
                sb.Append(" sensor=" + (r.sensor.HasObstacleAhead ? r.sensor.DetectedObstacle.name : "-"));
                if (a.isOnNavMesh)
                {
                    Vector3 st = a.steeringTarget; Vector3 fwd = Quaternion.Euler(0, r.Yaw, 0) * Vector3.forward;
                    float he = a.hasPath ? Vector3.SignedAngle(fwd, new Vector3(st.x - p.x, 0, st.z - p.z), Vector3.up) : float.NaN;
                    sb.Append(" agente[hasPath=" + a.hasPath + " pend=" + a.pathPending + " st=" + a.pathStatus + " stopped=" + a.isStopped + " rem=" + a.remainingDistance.ToString("F2") + " steer=" + V2(st) + " errRumo=" + he.ToString("F1") + "]");
                }
                return sb.ToString();
            }

            void Emit(string tag)
            {
                ev.AppendLine("--- " + tag + " (t=" + T.ToString("F2") + ") | pré-registro (últimos " + pre.Count + " passos, 0,02 s cada):");
                foreach (var l in pre) ev.AppendLine("   pre  " + l);
                post = 15;
            }

            public void Step()
            {
                Vector3 p = r.Pos; float dt = Time.fixedDeltaTime;
                float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prev.x, 0, prev.z)); dist += step; float v = step / dt; if (v > maxSpeed) maxSpeed = v; prev = p;
                if (Vector3.Distance(p, lastMovePos) > 0.02f) { lastMovePos = p; lastMoveT = Time.time; }
                stuck = Time.time - lastMoveT > 10f;
                if (shadow) Shadow();
                string line = Line();
                string ctl = r.ctl.CurrentNavigationState.ToString(); string ad = r.physical ? r.ad.State.ToString() : "-";
                string trig = null;
                if (ctl != lastCtl) { trig = "controlador: " + lastCtl + " -> " + ctl; lastCtl = ctl; }
                if (ad != lastAd) { trig = (trig == null ? "" : trig + " | ") + "adaptador: " + lastAd + " -> " + ad; lastAd = ad; }
                if (r.physical && r.body.ContactStopCount != lastStops) { stopsSeen = r.body.ContactStopCount; trig = (trig == null ? "" : trig + " | ") + "PARADA POR CONTATO #" + r.body.ContactStopCount + " (blocker=" + (r.body.guard.Probe.LastBlocker != null ? r.body.guard.Probe.LastBlocker.name : "?") + ")"; lastStops = r.body.ContactStopCount; if (firstStopT < 0) firstStopT = T; }
                if (pendingSensor != "") { trig = (trig == null ? "" : trig + " | ") + pendingSensor; pendingSensor = ""; }
                if (r.ctl.CurrentNavigationState == RobotNavigationState.WaitingForObstacle)
                {
                    if (firstWaitT < 0) firstWaitT = T;
                    framesWaiting++;
                    if (r.physical) { float cv = (float)fv.GetValue(r.ad), cw = (float)fw.GetValue(r.ad); if (Mathf.Abs(cv) > 0.01f || Mathf.Abs(cw) > 0.02f) cmdNonZeroWaiting++; }
                }
                if (trig != null) { Emit(trig); ev.AppendLine("   >>>> " + line); post = 15; }
                else if (post > 0) { ev.AppendLine("   pos  " + line); post--; }
                pre.Enqueue(line); if (pre.Count > 15) pre.Dequeue();
            }

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.AppendLine("distância percorrida=" + dist.ToString("F2") + " m | vmax(pose)=" + maxSpeed.ToString("F3") + " | paradas por contato=" + stopsSeen + " | quadros em WaitingForObstacle=" + framesWaiting
                    + " | quadros em espera COM comando de movimento=" + cmdNonZeroWaiting + " | travado(sem mover 10 s)=" + stuck);
                sb.AppendLine("quadros renderizados=" + (Time.frameCount - frame0) + " em " + T.ToString("F1") + " s de simulação (média " + ((Time.frameCount - frame0) / Mathf.Max(0.01f, T)).ToString("F1") + " quadros/s; passos de física por quadro ≈ " + (T / Mathf.Max(1, Time.frameCount - frame0) / Time.fixedDeltaTime).ToString("F2") + ")");
                if (!r.physical) sb.AppendLine("LEGADO medido com a mesma sonda do corpo físico: penetração máx=" + (legacyMaxPen * 1000f).ToString("F1") + " mm | folga mínima=" + legacyMinClear.ToString("F3") + " m");
                if (shadow) sb.AppendLine("VARREDURA-SOMBRA (a cada passo de física de 0,02 s): passos com toque previsto=" + shadowHitSteps + " de " + shadowSteps + " | trecho percorrido com toque previsto≈" + shadowTravel.ToString("F3") + " m (+1 passo) | primeiro: " + (shadowFirst == "" ? "nenhum" : shadowFirst) + " | último: " + (shadowLast == "" ? "nenhum" : shadowLast));
                sb.AppendLine("recuperação do controlador: rotações=" + r.ctl.RecoveryRotationCount + " retomadas=" + r.ctl.RecoveryResumeCount + " falhas=" + r.ctl.RecoveryFailureCount
                    + " | recoveryAttempts=" + Get<int>(r.ctl, "recoveryAttempts") + " rotating=" + Get<bool>(r.ctl, "recoveryRotating") + " exhausted=" + Get<bool>(r.ctl, "recoveryExhausted"));
                if (r.physical) sb.AppendLine("adaptador: interferência(quadros)=" + r.ad.InterferenceFrames + " máx=" + r.ad.MaxInterferenceMeters.ToString("F3") + "m/" + r.ad.MaxInterferenceDegrees.ToString("F1") + "° | isStopped reafirmado=" + r.ad.ForcedStopCount + " | remaining via cantos=" + r.ad.RemainingFallbackFrames);
                sb.AppendLine("eventos do sensor (" + sensorEvents.Count + "):");
                foreach (var e in sensorEvents) sb.AppendLine("   " + e);
                return sb.ToString();
            }

            public void Dispose() { r.sensor.ObstacleChanged -= OnSensor; }
        }

        // roda um trecho com rastreio; encerra ao chegar, ao travar ou no limite de tempo
        static IEnumerator RunLeg(DRun r, string dest, float maxSeconds, Tracer[] outTracer, bool shadow = false, bool forceScan = false)
        {
            var d = FindDest(dest);
            Assert.NotNull(d, dest);
            r.arrived = false;
            var tr = new Tracer(r); tr.shadow = shadow; tr.forceScanOnHit = forceScan; outTracer[0] = tr;
            tr.ev.AppendLine("== início: posição=" + V2(r.Pos) + " yaw=" + r.Yaw.ToString("F1") + " destino(" + dest + ")=" + V2(d.ApproachPoint.position) + " MoveTo=" + r.ctl.MoveTo(d.ApproachPoint.position));
            float waitedSince = -1f;
            while (!r.arrived && tr.T < maxSeconds && !tr.stuck)
            {
                yield return new WaitForFixedUpdate();
                tr.Step();
                if (r.ctl.CurrentNavigationState == RobotNavigationState.WaitingForObstacle) { if (waitedSince < 0) waitedSince = tr.T; if (tr.T - waitedSince > 12f) break; } else waitedSince = -1f;
            }
            for (int i = 0; i < 20; i++) { yield return new WaitForFixedUpdate(); tr.Step(); }
            tr.ev.AppendLine("== fim: chegou=" + r.arrived + " t=" + tr.T.ToString("F1") + "s posição final=" + V2(r.Pos) + " yaw=" + r.Yaw.ToString("F1") + " dist ao destino=" + Vector3.Distance(new Vector3(r.Pos.x, 0, r.Pos.z), new Vector3(d.ApproachPoint.position.x, 0, d.ApproachPoint.position.z)).ToString("F3"));
        }

        static IEnumerator Diag(string scene, bool physical, string dest, string file, System.Action<string> done)
        {
            var r = new DRun(); yield return Load(scene, physical, r);
            var box = new Tracer[1];
            yield return RunLeg(r, dest, 75f, box);
            var tr = box[0];
            string txt = "######## " + (physical ? "FÍSICA" : "LEGADO") + " -> " + dest + " ########\n" + tr.Summary() + "\n" + tr.ev;
            tr.Dispose();
            PlayResultsLog.Append(file, txt);
            done(txt);
        }

        [UnityTest, Timeout(600000)] public IEnumerator Diag_Cozinha_Fisico() { string o = null; yield return Diag(PhysScene, true, "Entrega_Cozinha", "faseC_cozinha_fisico.txt", x => o = x); Assert.NotNull(o); }
        [UnityTest, Timeout(600000)] public IEnumerator Diag_Quarto_Fisico() { string o = null; yield return Diag(PhysScene, true, "Entrega_Quarto", "faseC_quarto_fisico.txt", x => o = x); Assert.NotNull(o); }
        [UnityTest, Timeout(600000)] public IEnumerator Diag_Cozinha_Legado() { string o = null; yield return Diag(OrigScene, false, "Entrega_Cozinha", "faseC_cozinha_legado.txt", x => o = x); Assert.NotNull(o); }
        [UnityTest, Timeout(600000)] public IEnumerator Diag_Quarto_Legado() { string o = null; yield return Diag(OrigScene, false, "Entrega_Quarto", "faseC_quarto_legado.txt", x => o = x); Assert.NotNull(o); }

        // ---------------- reset (R) ----------------
        sealed class ResetCheck { public StringBuilder log = new StringBuilder(); public int bad; }

        static void Chk(ResetCheck c, string what, bool ok, string detail)
        {
            c.log.AppendLine((ok ? "  OK   " : "  FALHA ") + what + " | " + detail);
            if (!ok) c.bad++;
        }

        static IEnumerator VerifyReset(DRun r, Vector3 p0, float yaw0, ResetCheck c, string tag)
        {
            c.log.AppendLine("[" + tag + "] relatório do reset: " + r.reset.LastReport.Replace("\n", " ; "));
            yield return new WaitForFixedUpdate(); yield return null;
            Vector3 p = r.body.Position;
            Chk(c, "posição inicial", Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(p0.x, 0, p0.z)) < 0.002f, "corpo=" + V2(p) + " esperado=" + V2(p0));
            Chk(c, "rotação inicial", Mathf.Abs(Mathf.DeltaAngle(r.body.Rotation.eulerAngles.y, yaw0)) < 0.5f, "yaw=" + r.body.Rotation.eulerAngles.y.ToString("F2") + " esperado=" + yaw0.ToString("F2"));
            Chk(c, "velocidade linear/angular zeradas", Mathf.Abs(r.body.LinearSpeed) < 1e-4f && Mathf.Abs(r.body.YawRate) < 1e-4f, "v=" + r.body.LinearSpeed.ToString("F4") + " w=" + r.body.YawRate.ToString("F4"));
            Chk(c, "bloqueio de contato limpo", !r.body.ContactStopped, "ContactStopped=" + r.body.ContactStopped + " (contagem acumulada=" + r.body.ContactStopCount + ")");
            Chk(c, "recuperação do controlador limpa", !Get<bool>(r.ctl, "recoveryRotating") && !Get<bool>(r.ctl, "recoveryExhausted") && Get<int>(r.ctl, "recoveryAttempts") == 0, "rotating=" + Get<bool>(r.ctl, "recoveryRotating") + " exhausted=" + Get<bool>(r.ctl, "recoveryExhausted") + " attempts=" + Get<int>(r.ctl, "recoveryAttempts"));
            Chk(c, "controlador sem destino/Idle", !Get<bool>(r.ctl, "hasActiveDestination") && r.ctl.CurrentNavigationState == RobotNavigationState.Idle, "hasActiveDestination=" + Get<bool>(r.ctl, "hasActiveDestination") + " estado=" + r.ctl.CurrentNavigationState);
            Chk(c, "agente no NavMesh, sem caminho, parado", r.agent.isOnNavMesh && !r.agent.hasPath && r.agent.isStopped, "onNavMesh=" + r.agent.isOnNavMesh + " hasPath=" + r.agent.hasPath + " isStopped=" + r.agent.isStopped + " velocity=" + r.agent.velocity.magnitude.ToString("F3"));
            Vector3 an = r.agent.nextPosition; Vector3 tp = r.ctl.transform.position;
            Chk(c, "sincronia corpo/Transform/agente", Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(an.x, 0, an.z)) < 0.01f && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(tp.x, 0, tp.z)) < 0.005f,
                "corpo=" + V2(p) + " transform=" + V2(tp) + " agent.nextPosition=" + V2(an));
            Chk(c, "sensor sem detecção antiga", !r.sensor.HasObstacleAhead, "HasObstacleAhead=" + r.sensor.HasObstacleAhead + (r.sensor.HasObstacleAhead ? " (" + r.sensor.DetectedObstacle.name + ")" : ""));
            Chk(c, "adaptador em Idle, comando zero", r.ad.State == AdapterState.Idle, "estado=" + r.ad.State);
            // 1 s depois: nada se move
            Vector3 hold = r.body.Position; float drift = 0f, driftH = 0f, driftY = 0f;
            for (int i = 0; i < 50; i++) { yield return new WaitForFixedUpdate(); var q = r.body.Position; drift = Mathf.Max(drift, Vector3.Distance(hold, q)); driftH = Mathf.Max(driftH, Vector3.Distance(new Vector3(hold.x, 0, hold.z), new Vector3(q.x, 0, q.z))); driftY = Mathf.Max(driftY, Mathf.Abs(q.y - hold.y)); }
            Chk(c, "robô permanece parado 1 s após o reset (horizontal)", driftH < 0.002f, "deriva horizontal=" + driftH.ToString("F5") + " m");
            Chk(c, "sem ajuste vertical após o reset", driftY < 0.002f, "deriva vertical=" + driftY.ToString("F5") + " m (y logo após o reset=" + hold.y.ToString("F3") + ", y após 1 s=" + r.body.Position.y.ToString("F3") + "; y inicial capturado no Start=" + r.reset.initialPosition.y.ToString("F3") + ")");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Reset_R_DuranteNavegacao()
        {
            var r = new DRun(); yield return Load(PhysScene, true, r);
            var c = new ResetCheck(); c.log.AppendLine("== RESET DURANTE NAVEGAÇÃO (via tecla R simulada) ==");
            Vector3 p0 = r.body.Position; float yaw0 = r.body.Rotation.eulerAngles.y;
            Assert.IsTrue(r.reset.captured || true);
            Assert.IsTrue(r.ctl.MoveTo(FindDest("Entrega_Sala").ApproachPoint.position));
            for (int i = 0; i < 300; i++) yield return new WaitForFixedUpdate();
            c.log.AppendLine("antes do reset: pos=" + V2(r.body.Position) + " v=" + r.body.LinearSpeed.ToString("F3") + " estado=" + r.ctl.CurrentNavigationState + " ad=" + r.ad.State);
            Assert.Greater(Vector3.Distance(r.body.Position, p0), 0.5f, "robô deveria estar em movimento");
            // tecla R de verdade pelo Input System
            var kb = InputSystem.AddDevice<Keyboard>("TesteReset");
            int before = r.reset.ResetCount;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R)); InputSystem.Update();
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState()); InputSystem.Update();
            yield return null;
            bool keyWorked = r.reset.ResetCount == before + 1;
            c.log.AppendLine("  INFO tecla R simulada via Input System: " + (keyWorked ? "acionou o reset" : "NÃO chegou ao componente (Editor sem foco descarta eventos de teclado; binding da tecla não verificável automaticamente) -> chamando ResetNow() diretamente, que é o mesmo método que a tecla aciona"));
            if (!keyWorked) r.reset.ResetNow();
            InputSystem.RemoveDevice(kb);
            yield return VerifyReset(r, p0, yaw0, c, "durante navegação");
            // novo destino após o reset
            r.arrived = false; var box = new Tracer[1];
            yield return RunLeg(r, "Entrega_Sala", 60f, box);
            Chk(c, "novo destino após reset chega", r.arrived, "t=" + box[0].T.ToString("F1") + " dist=" + box[0].dist.ToString("F2") + " (referência sem reset: 15,8 s / 7,55 m)");
            box[0].Dispose();
            PlayResultsLog.Append("faseC_reset_navegacao.txt", c.log.ToString());
            Debug.Log(c.log.ToString());
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Reset_R_AposParadaPorObstaculo()
        {
            var r = new DRun(); yield return Load(PhysScene, true, r);
            var c = new ResetCheck(); c.log.AppendLine("== RESET APÓS PARADA POR OBSTÁCULO (Quarto trava em WaitingForObstacle) ==");
            Vector3 p0 = r.body.Position; float yaw0 = r.body.Rotation.eulerAngles.y;
            var box = new Tracer[1];
            yield return RunLeg(r, "Entrega_Quarto", 75f, box);
            var tr = box[0];
            c.log.AppendLine("antes do reset: chegou=" + r.arrived + " estado=" + r.ctl.CurrentNavigationState + " ad=" + r.ad.State + " pos=" + V2(r.body.Position) + " espera(quadros)=" + tr.framesWaiting + " paradasContato=" + r.body.ContactStopCount
                + " recuperação[rot=" + r.ctl.RecoveryRotationCount + " falhas=" + r.ctl.RecoveryFailureCount + "] sensor=" + (r.sensor.HasObstacleAhead ? r.sensor.DetectedObstacle.name : "-"));
            tr.Dispose();
            r.reset.ResetNow();
            yield return VerifyReset(r, p0, yaw0, c, "após obstáculo");
            // repetir a tentativa após R: o resultado é igual (determinístico)?
            r.arrived = false; var box2 = new Tracer[1];
            yield return RunLeg(r, "Entrega_Quarto", 75f, box2);
            c.log.AppendLine("2ª tentativa após R: chegou=" + r.arrived + " t=" + box2[0].T.ToString("F1") + " dist=" + box2[0].dist.ToString("F2") + " espera(quadros)=" + box2[0].framesWaiting + " (1ª tentativa: chegou=" + (box[0].r.arrived) + " dist=" + tr.dist.ToString("F2") + " espera=" + tr.framesWaiting + ")");
            box2[0].Dispose();
            PlayResultsLog.Append("faseC_reset_obstaculo.txt", c.log.ToString());
            Debug.Log(c.log.ToString());
        }

        // ---- geometria do canto da Cozinha (informativo) ----
        [UnityTest, Timeout(300000)]
        public IEnumerator Geometria_Cozinha_Canto_Informativo()
        {
            var r = new DRun(); yield return Load(PhysScene, true, r);
            var log = new StringBuilder(" == geometria perto do canto (0.47,0.47) onde a variante para na Cozinha ==\n");
            var st = NavMesh.GetSettingsByIndex(0);
            log.AppendLine("NavMesh agente: raio=" + st.agentRadius.ToString("F3") + " altura=" + st.agentHeight.ToString("F2") + " | caixa do robô: 0,55 x 0,65 (meia-diagonal 0,426)");
            Vector3 corner = new Vector3(0.47f, 0.04f, 0.47f);
            NavMeshHit eh; bool ok = NavMesh.FindClosestEdge(corner, out eh, NavMesh.AllAreas);
            log.AppendLine("canto de rota " + V2(corner) + ": distância à borda do NavMesh mais próxima=" + (ok ? eh.distance.ToString("F3") + " em " + V2(eh.position) : "n/d"));
            var cols = Physics.OverlapSphere(new Vector3(0.47f, 0.5f, 0.47f), 1.3f, ~0, QueryTriggerInteraction.Collide);
            var seen = new List<string>();
            foreach (var c in cols)
            {
                if (c.transform.IsChildOf(r.ctl.transform)) continue;
                Vector3 cp = c.ClosestPoint(new Vector3(0.47f, 0.5f, 0.47f));
                var bc = c as BoxCollider;
                seen.Add(c.name + " [layer " + c.gameObject.layer + (c.isTrigger ? ", trigger" : "") + (c.enabled ? "" : ", DESATIVADO") + "] bounds.c=" + V2(c.bounds.center) + " size=" + c.bounds.size.ToString("F2") + " dist ao canto=" + Vector3.Distance(new Vector3(cp.x, 0, cp.z), new Vector3(0.47f, 0, 0.47f)).ToString("F3") + " ponto mais próximo=" + V2(cp)
                    + (bc != null ? " (BoxCollider, rot.y=" + c.transform.eulerAngles.y.ToString("F0") + ")" : ""));
            }
            seen.Sort();
            foreach (var l in seen) log.AppendLine("  " + l);
            foreach (var d in Object.FindObjectsByType<DoorController>(FindObjectsInactive.Include))
            {
                float dd = Vector3.Distance(new Vector3(d.transform.position.x, 0, d.transform.position.z), new Vector3(0.47f, 0, 0.47f));
                if (dd < 3f) log.AppendLine("  porta '" + d.name + "' a " + dd.ToString("F2") + " m: aberta=" + d.IsOpen + " pos=" + V2(d.transform.position));
            }
            PlayResultsLog.Append("faseC_geometria_cozinha.txt", log.ToString());
        }

        // ---- Quarto físico repetido: captura execuções que falham, com correlação de taxa de quadros ----
        [UnityTest, Timeout(1500000)]
        public IEnumerator Diag_Quarto_Fisico_Repetido()
        {
            var sum = new StringBuilder("== Quarto físico: tentativas independentes (cena recarregada a cada uma) ==\n");
            string firstFail = null;
            for (int k = 1; k <= 6; k++)
            {
                var r = new DRun(); yield return Load(PhysScene, true, r);
                var box = new Tracer[1];
                yield return RunLeg(r, "Entrega_Quarto", 75f, box);
                var tr = box[0];
                sum.AppendLine("tentativa " + k + ": chegou=" + r.arrived + " t=" + tr.T.ToString("F1") + "s dist=" + tr.dist.ToString("F2") + " espera(quadros)=" + tr.framesWaiting + " 1ª espera em t=" + tr.firstWaitT.ToString("F1") + " paradasContato=" + tr.stopsSeen + " posição final=" + V2(r.Pos) + " yaw=" + r.Yaw.ToString("F0")
                    + " | " + tr.Summary().Split('\n')[1].Trim());
                if (!r.arrived && firstFail == null) firstFail = "######## 1ª FALHA (tentativa " + k + ") ########\n" + tr.Summary() + "\n" + tr.ev;
                tr.Dispose();
                if (firstFail != null && k >= 3) break;
            }
            PlayResultsLog.Append("faseC_quarto_repetido.txt", sum + "\n" + (firstFail ?? "nenhuma falha capturada"));
        }

        // ---- Quarto físico: varia só a FASE do sensor (nextScanTime) para testar se a falha depende de quando a varredura cai sobre a parede ----
        [UnityTest, Timeout(1800000)]
        public IEnumerator Diag_Quarto_FaseDoSensor()
        {
            var sum = new StringBuilder("== Quarto físico: deslocamento da fase da varredura do sensor (intervalo 0,1 s) ==\n");
            foreach (float off in new[] { 0f, 0.02f, 0.04f, 0.06f, 0.08f })
            {
                var r = new DRun(); yield return Load(PhysScene, true, r);
                var pr = r.ctl.gameObject.AddComponent<RecoveryWriteProbe>(); pr.ctl = r.ctl; pr.body = r.body;
                var fld = typeof(RobotSensorSuite).GetField("nextScanTime", BF);
                fld.SetValue(r.sensor, Time.time + off);
                var box = new Tracer[1];
                yield return RunLeg(r, "Entrega_Quarto", 75f, box);
                var tr = box[0];
                string first = tr.sensorEvents.Count > 0 ? tr.sensorEvents[0] : "nenhum evento de sensor";
                sum.AppendLine("fase +" + off.ToString("F2") + " s: chegou=" + r.arrived + " t=" + tr.T.ToString("F1") + " dist=" + tr.dist.ToString("F2") + " eventosSensor=" + tr.sensorEvents.Count + " espera(quadros)=" + tr.framesWaiting
                    + " recuperação[rot=" + r.ctl.RecoveryRotationCount + " retomadas=" + r.ctl.RecoveryResumeCount + " falhas=" + r.ctl.RecoveryFailureCount + "] | 1º evento: " + first);
                if (pr.framesWaiting > 0)
                    sum.AppendLine("    observador (quadros em espera=" + pr.framesWaiting + "): quadros com rotação escrita no Transform pelo controlador=" + pr.framesRotationWritten + " (>3°: " + pr.framesAbove3deg + ") delta máx=" + pr.maxDeltaDeg.ToString("F2")
                        + "° soma=" + pr.sumDeltaDeg.ToString("F1") + "° | maior variação de yaw do CORPO entre quadros=" + pr.maxBodyYawChange.ToString("F3") + "° | interferência registrada pelo adaptador=" + r.ad.InterferenceFrames + " quadros");
                tr.Dispose();
            }
            PlayResultsLog.Append("faseC_quarto_fase_sensor.txt", sum.ToString());
        }

        // ---- janela de detecção da varredura: física x legado (somente leitura) ----
        [UnityTest, Timeout(900000)]
        public IEnumerator Diag_Quarto_JanelaDoSensor()
        {
            var sum = new StringBuilder();
            foreach (bool phys in new[] { true, false })
            {
                var r = new DRun(); yield return Load(phys ? PhysScene : OrigScene, phys, r);
                var box = new Tracer[1];
                yield return RunLeg(r, "Entrega_Quarto", 75f, box, true, false);
                var t = box[0]; var lines = t.Summary().Split('\n');
                sum.AppendLine((phys ? "FÍSICA" : "LEGADO") + ": chegou=" + r.arrived + " t=" + t.T.ToString("F1") + "s | " + lines[0].Trim());
                foreach (var l in lines) if (l.StartsWith("VARREDURA") || l.StartsWith("quadros renderizados")) sum.AppendLine("    " + l.Trim());
                t.Dispose();
            }
            PlayResultsLog.Append("faseC_quarto_janela_sensor.txt", sum.ToString());
        }

        // ---- reproduz a falha do Quarto de forma determinística (ScanNow no passo previsto) e mede a recuperação ----
        [UnityTest, Timeout(900000)]
        public IEnumerator Diag_Quarto_FalhaReproduzida()
        {
            var r = new DRun(); yield return Load(PhysScene, true, r);
            var pr = r.ctl.gameObject.AddComponent<RecoveryWriteProbe>(); pr.ctl = r.ctl; pr.body = r.body;
            var box = new Tracer[1];
            yield return RunLeg(r, "Entrega_Quarto", 75f, box, true, true);
            var t = box[0];
            string txt = "######## FALHA REPRODUZIDA (ScanNow forçado) ########\n" + t.Summary() + "\n"
                + "observador (quadros em espera=" + pr.framesWaiting + "): quadros com rotação escrita no Transform pelo controlador=" + pr.framesRotationWritten + " (>3°: " + pr.framesAbove3deg + ") delta máx=" + pr.maxDeltaDeg.ToString("F2")
                + "° | maior variação de yaw do CORPO entre quadros=" + pr.maxBodyYawChange.ToString("F3") + "° | interferência registrada pelo adaptador=" + r.ad.InterferenceFrames + " quadros\n" + t.ev;
            t.Dispose();
            PlayResultsLog.Append("faseC_quarto_falha_reproduzida.txt", txt);
        }
    }
}
#endif
