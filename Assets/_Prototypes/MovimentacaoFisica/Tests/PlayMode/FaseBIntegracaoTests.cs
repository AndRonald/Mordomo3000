#if UNITY_EDITOR
using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    // Fase B: robô variante físico na CÓPIA da cena do bairro (controlador legado decide, adaptador move).
    public class FaseBIntegracaoTests
    {
        const string ScenePath = "Assets/_Prototypes/MovimentacaoFisica/Scenes/CasaGrandeBairroScene_Fisica.unity";

        sealed class Run
        {
            public RobotNavigationController ctl; public PhysicalDriveAdapter ad; public KinematicDriveBody body; public NavMeshAgent agent;
            public bool arrived; public float arrivedAt;
        }

        static IEnumerator Load(Run r)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            r.ctl = Object.FindAnyObjectByType<RobotNavigationController>();
            Assert.NotNull(r.ctl, "controlador");
            r.ad = r.ctl.GetComponent<PhysicalDriveAdapter>();
            r.body = r.ctl.GetComponent<KinematicDriveBody>();
            r.agent = r.ctl.GetComponent<NavMeshAgent>();
            Assert.NotNull(r.ad, "adaptador"); Assert.NotNull(r.body, "corpo"); Assert.NotNull(r.agent, "agente");
            r.ctl.SetKeyboardShortcutsEnabled(false);
            r.ctl.Arrived += () => { r.arrived = true; r.arrivedAt = Time.time; };
            // esperar o agente assentar no NavMesh
            float t0 = Time.time;
            while (!r.agent.isOnNavMesh && Time.time - t0 < 3f) yield return null;
            Assert.IsTrue(r.agent.isOnNavMesh, "agente fora do NavMesh");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FaseB_RotaCurta_Pickup()
        {
            var r = new Run();
            yield return Load(r);
            var log = new StringBuilder();
            log.AppendLine("== Fase B / rota curta (cópia do bairro, variante física) ==");
            Assert.IsTrue(r.ad.ExternallyDriven, "ponte não conectou ExternalNavigating");

            var goal = GameObject.Find("Casa/Central_Recebimento/Ponto_Coleta");
            Assert.NotNull(goal, "Ponto_Coleta");
            Vector3 start = r.body.Position;
            log.AppendLine("início físico=" + start.ToString("F3") + " transform=" + r.ctl.transform.position.ToString("F3")
                + " destino=" + goal.transform.position.ToString("F3") + " isStopped(antes)=" + r.agent.isStopped);

            Assert.IsTrue(r.ctl.MoveTo(goal.transform.position), "MoveTo falhou");

            float dt = Time.fixedDeltaTime;
            Vector3 prev = r.body.Position;
            float maxSpeed = 0f, maxDivAgent = 0f, maxTfBody = 0f, maxPen = 0f, dist = 0f;
            int frames = 0, movingFrames = 0, notStoppedFrames = 0, badSteering = 0, nonFiniteRemaining = 0, incompletePath = 0;
            float firstRemaining = float.NaN, lastRemaining = float.NaN;
            float t0 = Time.time;
            string lastState = "";
            while (!r.arrived && Time.time - t0 < 90f)
            {
                yield return new WaitForFixedUpdate();
                frames++;
                Vector3 p = r.body.Position;
                float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prev.x, 0, prev.z));
                float v = step / dt; dist += step; prev = p;
                if (v > maxSpeed) maxSpeed = v;
                if (v > 0.02f) movingFrames++;
                Vector3 an = r.agent.nextPosition;
                float dAg = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(an.x, 0, an.z));
                if (dAg > maxDivAgent) maxDivAgent = dAg;
                var tp = r.ctl.transform.position;
                float dTf = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(tp.x, 0, tp.z));
                if (dTf > maxTfBody) maxTfBody = dTf;
                float pen = r.body.guard.Probe.MeasurePenetration(p, r.body.Rotation);
                if (pen > maxPen) maxPen = pen;

                if (r.ctl.CurrentNavigationState == RobotNavigationState.Navigating && r.agent.hasPath && !r.agent.pathPending)
                {
                    if (!r.agent.isStopped) notStoppedFrames++;
                    float rem = r.agent.remainingDistance;
                    if (float.IsInfinity(rem) || float.IsNaN(rem)) nonFiniteRemaining++;
                    else { if (float.IsNaN(firstRemaining)) firstRemaining = rem; lastRemaining = rem; }
                    if ((r.agent.steeringTarget - r.agent.nextPosition).sqrMagnitude < 1e-8f) badSteering++;
                    if (r.agent.pathStatus != NavMeshPathStatus.PathComplete) incompletePath++;
                }
                string st = r.ctl.CurrentNavigationState.ToString() + "/" + r.ad.State;
                if (st != lastState) { log.AppendLine("t=" + (Time.time - t0).ToString("F2") + " estado " + st + " pos=" + p.ToString("F2")); lastState = st; }
            }
            float arrivalTime = Time.time - t0;
            Vector3 endPos = r.body.Position;
            // assentamento: o controlador declara chegada a <=0,10 m; o corpo ainda freia até parar (medido à parte)
            float settleStart = Time.time; float settleDist = 0f; Vector3 sp = endPos;
            while (Mathf.Abs(r.body.LinearSpeed) > 0.005f && Time.time - settleStart < 2f)
            { yield return new WaitForFixedUpdate(); var q = r.body.Position; settleDist += Vector3.Distance(sp, q); sp = q; }
            float settleTime = Time.time - settleStart;
            // depois de parado: 1 s sem nenhum movimento
            Vector3 hold = r.body.Position; float holdMaxDrift = 0f;
            for (int i = 0; i < Mathf.RoundToInt(1f / dt); i++)
            {
                yield return new WaitForFixedUpdate();
                float d = Vector3.Distance(r.body.Position, hold); if (d > holdMaxDrift) holdMaxDrift = d;
            }
            float goalErr = Vector3.Distance(new Vector3(endPos.x, 0, endPos.z), new Vector3(goal.transform.position.x, 0, goal.transform.position.z));

            log.AppendLine("chegou=" + r.arrived + " t=" + arrivalTime.ToString("F2") + "s dist=" + dist.ToString("F2") + "m frames=" + frames + " frames em movimento=" + movingFrames);
            log.AppendLine("assentamento após o evento de chegada: " + settleDist.ToString("F3") + " m em " + settleTime.ToString("F2") + " s | erro final ao destino=" + Vector3.Distance(new Vector3(r.body.Position.x, 0, r.body.Position.z), new Vector3(goal.transform.position.x, 0, goal.transform.position.z)).ToString("F3") + " m");
            log.AppendLine("erro ao destino=" + goalErr.ToString("F3") + "m | deriva após chegada (1 s)=" + holdMaxDrift.ToString("F5") + "m");
            log.AppendLine("vel real máx (pose física)=" + maxSpeed.ToString("F3") + " m/s | adaptador MaxRealSpeed=" + r.ad.MaxRealSpeed.ToString("F3") + " yawMax=" + r.ad.MaxYawRateDegPerSec.ToString("F1") + "°/s");
            log.AppendLine("divergência agente x corpo: máx=" + maxDivAgent.ToString("F4") + "m (adaptador MaxDivergence=" + r.ad.MaxDivergence.ToString("F4") + " pós-sync=" + r.ad.MaxPostSyncDivergence.ToString("F5") + ")");
            log.AppendLine("divergência transform x corpo (disputa de Transform): máx=" + maxTfBody.ToString("F5") + "m");
            log.AppendLine("interferência externa: quadros=" + r.ad.InterferenceFrames + " máx=" + r.ad.MaxInterferenceMeters.ToString("F4") + "m/" + r.ad.MaxInterferenceDegrees.ToString("F2") + "° | isStopped reafirmado " + r.ad.ForcedStopCount + "x");
            log.AppendLine("agente com isStopped=false durante Navigating: " + notStoppedFrames + " quadros | steeringTarget degenerado: " + badSteering + " | remainingDistance inválido: " + nonFiniteRemaining + " | pathStatus != Complete: " + incompletePath);
            log.AppendLine("remainingDistance: inicial=" + firstRemaining.ToString("F2") + " final=" + lastRemaining.ToString("F2"));
            log.AppendLine("penetração máx=" + (maxPen * 1000f).ToString("F2") + " mm | custo passo corpo médio=" + r.body.AverageStepMicroseconds.ToString("F1") + " µs (máx " + r.body.MaxStepMicroseconds.ToString("F0") + ") | contactStops=" + r.body.ContactStopCount);
            PlayResultsLog.Append("faseB_rota_curta.txt", log.ToString());
            Debug.Log(log.ToString());

            Assert.IsTrue(r.arrived, "controlador não declarou chegada em 90 s");
            Assert.LessOrEqual(maxSpeed, 0.605f, "velocidade real");
            Assert.LessOrEqual(maxPen, 0.001f, "penetração");
            Assert.LessOrEqual(settleTime, 1.0f, "tempo de assentamento");
            Assert.LessOrEqual(settleDist, 0.20f, "distância de assentamento");
            Assert.LessOrEqual(holdMaxDrift, 0.002f, "movimento após parar");
            Assert.LessOrEqual(r.body.AverageStepMicroseconds, 1000f, "custo do passo");
        }

        // ---- interferência: o adaptador deve DETECTAR e DESFAZER qualquer movimento externo do Transform / isStopped ----
        [UnityTest, Timeout(300000)]
        public IEnumerator FaseB_Interferencia_Detectada_E_Desfeita()
        {
            var r = new Run();
            yield return Load(r);
            var goal = GameObject.Find("Casa/Central_Recebimento/Ponto_Coleta");
            Assert.IsTrue(r.ctl.MoveTo(goal.transform.position));
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();   // já em movimento
            int before = r.ad.InterferenceFrames, stopsBefore = r.ad.ForcedStopCount;
            Vector3 bp = r.body.Position;

            yield return null;   // fase de Update (onde o controlador legado age); após FixedUpdate a interpolação do Rigidbody apagaria o empurrão
            // (1) empurra o Transform 0,25 m para o lado (como o controlador legado faria numa recuperação)
            r.ctl.transform.position += r.ctl.transform.right * 0.25f;
            yield return null; yield return new WaitForFixedUpdate();
            int afterPush = r.ad.InterferenceFrames;
            float tfErr = Vector3.Distance(new Vector3(r.ctl.transform.position.x, 0, r.ctl.transform.position.z), new Vector3(r.body.Position.x, 0, r.body.Position.z));

            // (2) gira o Transform 40°
            yield return null;
            r.ctl.transform.rotation = Quaternion.Euler(0f, r.ctl.transform.eulerAngles.y + 40f, 0f);
            yield return null; yield return new WaitForFixedUpdate();
            int afterRot = r.ad.InterferenceFrames;

            yield return null;
            // (3) solta isStopped (como MoveTo/Resume do legado fazem)
            r.agent.isStopped = false;
            yield return null; yield return new WaitForFixedUpdate();
            bool stoppedAgain = r.agent.isStopped;
            int stopsAfter = r.ad.ForcedStopCount;

            var log = new StringBuilder();
            log.AppendLine("== Fase B / interferência ==");
            log.AppendLine("empurrão 0,25 m: quadros de interferência +" + (afterPush - before) + " | máx registrado=" + r.ad.MaxInterferenceMeters.ToString("F3") + " m | transform x corpo após desfazer=" + tfErr.ToString("F5") + " m");
            log.AppendLine("rotação 40°: +" + (afterRot - afterPush) + " quadros | máx registrado=" + r.ad.MaxInterferenceDegrees.ToString("F1") + "°");
            log.AppendLine("isStopped solto: reafirmado="+ stoppedAgain + " (ForcedStopCount +" + (stopsAfter - stopsBefore) + ")");
            PlayResultsLog.Append("faseB_interferencia.txt", log.ToString());
            Debug.Log(log.ToString());

            Assert.Greater(afterPush - before, 0, "empurrão não detectado");
            Assert.GreaterOrEqual(r.ad.MaxInterferenceMeters, 0.2f, "magnitude do empurrão");
            Assert.LessOrEqual(tfErr, 0.001f, "transform não foi devolvido ao corpo");
            Assert.Greater(afterRot - afterPush, 0, "rotação não detectada");
            Assert.GreaterOrEqual(r.ad.MaxInterferenceDegrees, 30f, "magnitude da rotação");
            Assert.IsTrue(stoppedAgain, "isStopped não foi reafirmado");
            Assert.Greater(stopsAfter - stopsBefore, 0, "ForcedStopCount não contou");
        }

        // ---- seis destinos em sequência (somente o controlador legado: MoveTo + Arrived; sem ciclo de entrega) ----
        [UnityTest, Timeout(900000)]
        public IEnumerator FaseB_SeisDestinos_Sequencia()
        {
            var r = new Run();
            yield return Load(r);
            var log = new StringBuilder();
            log.AppendLine("== Fase B / seis destinos (MoveTo direto, sem PackageDeliveryManager) ==");
            var dests = Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include);
            string[] order = { "Entrega_Sala", "Entrega_Jantar", "Entrega_Cozinha", "Entrega_Quarto", "Entrega_Quarto2", "Entrega_Escritorio" };
            float dt = Time.fixedDeltaTime;
            float gMaxSpeed = 0f, gMaxPen = 0f, gMaxDiv = 0f, gMaxTf = 0f; int fails = 0;
            var results = new StringBuilder();
            foreach (var nm in order)
            {
                DeliveryDestination d = null;
                foreach (var x in dests) if (x != null && x.name == nm) { d = x; break; }
                if (d == null) { log.AppendLine(nm + ": NÃO ENCONTRADO"); fails++; continue; }
                Vector3 target = d.ApproachPoint.position;
                r.arrived = false;
                if (!r.ctl.MoveTo(target)) { log.AppendLine(nm + ": MoveTo recusado (sem rota completa)"); fails++; continue; }
                Vector3 prev = r.body.Position; float dist = 0f, vmax = 0f, pmax = 0f, dmax = 0f, tmax = 0f; float t0 = Time.time;
                int wait = 0;
                while (!r.arrived && Time.time - t0 < 120f)
                {
                    yield return new WaitForFixedUpdate();
                    Vector3 p = r.body.Position;
                    float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prev.x, 0, prev.z));
                    dist += step; float v = step / dt; if (v > vmax) vmax = v; prev = p;
                    float pen = r.body.guard.Probe.MeasurePenetration(p, r.body.Rotation); if (pen > pmax) pmax = pen;
                    Vector3 an = r.agent.nextPosition; float dd = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(an.x, 0, an.z)); if (dd > dmax) dmax = dd;
                    Vector3 tp = r.ctl.transform.position; float tt = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(tp.x, 0, tp.z)); if (tt > tmax) tmax = tt;
                    if (r.ctl.CurrentNavigationState == RobotNavigationState.WaitingForObstacle || r.ctl.CurrentNavigationState == RobotNavigationState.WaitingForRoute) wait++;
                }
                float tArr = Time.time - t0;
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();   // assenta
                Vector3 e = r.body.Position;
                float err = Vector3.Distance(new Vector3(e.x, 0, e.z), new Vector3(target.x, 0, target.z));
                bool ok = r.arrived;
                if (!ok) fails++;
                if (vmax > gMaxSpeed) gMaxSpeed = vmax; if (pmax > gMaxPen) gMaxPen = pmax; if (dmax > gMaxDiv) gMaxDiv = dmax; if (tmax > gMaxTf) gMaxTf = tmax;
                log.AppendLine(nm + ": chegou=" + ok + " t=" + tArr.ToString("F1") + "s dist=" + dist.ToString("F2") + "m vmax=" + vmax.ToString("F3") + " penet=" + (pmax * 1000f).ToString("F2") + "mm div(agente)=" + dmax.ToString("F3") + "m transform x corpo=" + tmax.ToString("F4") + "m erro final=" + err.ToString("F3") + "m quadros em espera=" + wait);
            }
            log.AppendLine("GLOBAL: vmax=" + gMaxSpeed.ToString("F3") + " penetração máx=" + (gMaxPen * 1000f).ToString("F2") + "mm div agente máx=" + gMaxDiv.ToString("F3") + "m transform x corpo máx=" + gMaxTf.ToString("F4") + "m | interferência: quadros=" + r.ad.InterferenceFrames + " | contactStops=" + r.body.ContactStopCount + " | passo médio=" + r.body.AverageStepMicroseconds.ToString("F1") + " µs (máx " + r.body.MaxStepMicroseconds.ToString("F0") + ") | falhas="+ fails);
            PlayResultsLog.Append("faseB_seis_destinos.txt", log.ToString());
            Debug.Log(log.ToString());

            Assert.AreEqual(0, fails, "destinos com falha (ver faseB_seis_destinos.txt)");
            Assert.LessOrEqual(gMaxSpeed, 0.605f, "velocidade real");
            Assert.LessOrEqual(gMaxPen, 0.001f, "penetração");
            Assert.LessOrEqual(r.body.AverageStepMicroseconds, 1000f, "custo do passo");
        }

        // ---- diagnóstico (informativo): por que o robô não saiu do lugar rumo a Entrega_Jantar depois de Entrega_Sala ----
        [UnityTest, Timeout(300000)]
        public IEnumerator FaseB_Diagnostico_Jantar_Informativo()
        {
            var r = new Run();
            yield return Load(r);
            var log = new StringBuilder();
            log.AppendLine("== diagnóstico Sala -> Jantar ==");
            var dests = Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include);
            DeliveryDestination sala = null, jantar = null;
            foreach (var x in dests) { if (x.name == "Entrega_Sala") sala = x; if (x.name == "Entrega_Jantar") jantar = x; }
            var fv = typeof(PhysicalDriveAdapter).GetField("_cmdV", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fw = typeof(PhysicalDriveAdapter).GetField("_cmdW", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsTrue(r.ctl.MoveTo(sala.ApproachPoint.position));
            float t0 = Time.time; while (!r.arrived && Time.time - t0 < 60f) yield return new WaitForFixedUpdate();
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
            r.arrived = false;
            bool ok = r.ctl.MoveTo(jantar.ApproachPoint.position);
            log.AppendLine("MoveTo(Jantar)=" + ok + " destino=" + jantar.ApproachPoint.position.ToString("F2") + " pos=" + r.body.Position.ToString("F3") + " yaw=" + r.body.Rotation.eulerAngles.y.ToString("F1"));
            for (int k = 0; k < 12; k++)
            {
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                var a = r.agent; var b = r.body;
                float hdg = Vector3.SignedAngle(b.Rotation * Vector3.forward, new Vector3(a.steeringTarget.x - b.Position.x, 0, a.steeringTarget.z - b.Position.z), Vector3.up);
                log.AppendLine("t=" + (k * 0.5f + 0.5f).ToString("F1") + " ctl=" + r.ctl.CurrentNavigationState + " ad=" + r.ad.State + " hasPath=" + a.hasPath + " pending=" + a.pathPending + " status=" + a.pathStatus + " rem=" + a.remainingDistance.ToString("F2") + " isStopped=" + a.isStopped
                    + " steer=" + a.steeringTarget.ToString("F2") + " errHdg=" + hdg.ToString("F1") + " cmd(v,w)=(" + ((float)fv.GetValue(r.ad)).ToString("F2") + "," + ((float)fw.GetValue(r.ad)).ToString("F2") + ") speed=" + b.LinearSpeed.ToString("F3") + " yawRate=" + b.YawRate.ToString("F3")
                    + " yaw=" + b.Rotation.eulerAngles.y.ToString("F1") + " contactStopped=" + b.ContactStopped + " stops=" + b.ContactStopCount + " clear=" + b.guard.Probe.MeasureClearance(b.Position, b.Rotation).ToString("F3") + " pen=" + b.guard.Probe.MeasurePenetration(b.Position, b.Rotation).ToString("F4") + " blocker=" + (b.guard.Probe.LastBlocker != null ? b.guard.Probe.LastBlocker.name : "-"));
            }
            PlayResultsLog.Append("faseB_diagnostico_jantar.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        // ---- referência: mesma sequência na cena ORIGINAL (somente leitura, carregada em memória, nada é salvo) ----
        [UnityTest, Timeout(900000)]
        public IEnumerator Referencia_Legado_SeisDestinos_CenaOriginal()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/CasaGrandeBairroScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var ctl = Object.FindAnyObjectByType<RobotNavigationController>();
            var agent = ctl.GetComponent<NavMeshAgent>();
            Assert.IsNull(ctl.GetComponent<PhysicalDriveAdapter>(), "cena original não deve ter o adaptador");
            ctl.SetKeyboardShortcutsEnabled(false);
            bool arrived = false; ctl.Arrived += () => arrived = true;
            float tw = Time.time; while (!agent.isOnNavMesh && Time.time - tw < 3f) yield return null;
            var log = new StringBuilder();
            log.AppendLine("== REFERÊNCIA: controlador legado, cena original, mesma sequência ==");
            var dests = Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include);
            string[] order = { "Entrega_Sala", "Entrega_Jantar", "Entrega_Cozinha", "Entrega_Quarto", "Entrega_Quarto2", "Entrega_Escritorio" };
            float dt = Time.fixedDeltaTime;
            foreach (var nm in order)
            {
                DeliveryDestination d = null; foreach (var x in dests) if (x != null && x.name == nm) { d = x; break; }
                if (d == null) { log.AppendLine(nm + ": não encontrado"); continue; }
                Vector3 target = d.ApproachPoint.position; arrived = false;
                if (!ctl.MoveTo(target)) { log.AppendLine(nm + ": MoveTo recusado"); continue; }
                Vector3 prev = ctl.transform.position; float dist = 0f, vmax = 0f, t0 = Time.time; int wait = 0;
                while (!arrived && Time.time - t0 < 120f)
                {
                    yield return new WaitForFixedUpdate();
                    Vector3 p = ctl.transform.position; float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prev.x, 0, prev.z));
                    dist += step; float v = step / dt; if (v > vmax) vmax = v; prev = p;
                    var st = ctl.CurrentNavigationState; if (st == RobotNavigationState.WaitingForObstacle || st == RobotNavigationState.WaitingForRoute) wait++;
                }
                float tArr = Time.time - t0;
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                Vector3 e = ctl.transform.position;
                log.AppendLine(nm + ": chegou=" + arrived + " t=" + tArr.ToString("F1") + "s dist=" + dist.ToString("F2") + "m vmax(pose)=" + vmax.ToString("F3") + " erro final=" + Vector3.Distance(new Vector3(e.x, 0, e.z), new Vector3(target.x, 0, target.z)).ToString("F3") + "m quadros em espera=" + wait);
            }
            PlayResultsLog.Append("faseB_referencia_legado.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        // ---- destinos INDEPENDENTES: cena recarregada a cada trecho (estado limpo), mesmo roteiro nos dois robôs ----
        static readonly string[] AllDest = { "Entrega_Sala", "Entrega_Jantar", "Entrega_Cozinha", "Entrega_Quarto", "Entrega_Quarto2", "Entrega_Escritorio" };

        static IEnumerator IndependentLegs(string scenePath, bool physical, string outFile, System.Action<string> done)
        {
            var log = new StringBuilder();
            log.AppendLine("== destinos independentes | " + (physical ? "VARIANTE FÍSICA (cópia)" : "LEGADO (original)") + " ==");
            float gV = 0f, gPen = 0f; int fails = 0; double stepAvg = 0; int stepN = 0;
            foreach (var nm in AllDest)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
                yield return null; yield return null;
                var ctl = Object.FindAnyObjectByType<RobotNavigationController>();
                var agent = ctl.GetComponent<NavMeshAgent>();
                var body = ctl.GetComponent<KinematicDriveBody>(); var ad = ctl.GetComponent<PhysicalDriveAdapter>();
                Assert.AreEqual(physical, ad != null, "tipo de robô na cena");
                ctl.SetKeyboardShortcutsEnabled(false);
                bool arrived = false; ctl.Arrived += () => arrived = true;
                float tw = Time.time; while (!agent.isOnNavMesh && Time.time - tw < 3f) yield return null;
                DeliveryDestination d = null;
                foreach (var x in Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include)) if (x.name == nm) { d = x; break; }
                if (d == null) { log.AppendLine(nm + ": não encontrado"); fails++; continue; }
                Vector3 target = d.ApproachPoint.position;
                if (!ctl.MoveTo(target)) { log.AppendLine(nm + ": MoveTo recusado"); fails++; continue; }
                float dt = Time.fixedDeltaTime;
                Vector3 prev = physical ? body.Position : ctl.transform.position;
                float dist = 0f, vmax = 0f, pmax = 0f, t0 = Time.time; int wait = 0;
                while (!arrived && Time.time - t0 < 90f)
                {
                    yield return new WaitForFixedUpdate();
                    Vector3 p = physical ? body.Position : ctl.transform.position;
                    float step = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(prev.x, 0, prev.z));
                    dist += step; float v = step / dt; if (v > vmax) vmax = v; prev = p;
                    if (physical) { float pen = body.guard.Probe.MeasurePenetration(p, body.Rotation); if (pen > pmax) pmax = pen; }
                    var st = ctl.CurrentNavigationState; if (st == RobotNavigationState.WaitingForObstacle || st == RobotNavigationState.WaitingForRoute) wait++;
                }
                float tArr = Time.time - t0;
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                Vector3 e = physical ? body.Position : ctl.transform.position;
                float err = Vector3.Distance(new Vector3(e.x, 0, e.z), new Vector3(target.x, 0, target.z));
                if (!arrived) fails++;
                if (vmax > gV) gV = vmax; if (pmax > gPen) gPen = pmax;
                string extra = physical ? " penet=" + (pmax * 1000f).ToString("F2") + "mm interf=" + ad.InterferenceFrames + " fallbackRem=" + ad.RemainingFallbackFrames + " stops=" + body.ContactStopCount : "";
                if (physical) { stepAvg += body.AverageStepMicroseconds; stepN++; }
                log.AppendLine(nm + ": chegou=" + arrived + " t=" + tArr.ToString("F1") + "s dist=" + dist.ToString("F2") + "m vmax=" + vmax.ToString("F3") + " erro final=" + err.ToString("F3") + "m espera(quadros)=" + wait + extra);
            }
            log.AppendLine("RESUMO: falhas=" + fails + " vmax global=" + gV.ToString("F3") + " penetração máx=" + (gPen * 1000f).ToString("F2") + "mm" + (stepN > 0 ? " passo médio=" + (stepAvg / stepN).ToString("F1") + " µs" : ""));
            PlayResultsLog.Append(outFile, log.ToString());
            Debug.Log(log.ToString());
            done(fails + ":" + gV.ToString("F3") + ":" + gPen.ToString("F5"));
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator FaseB_Destinos_Independentes_Fisico()
        {
            string res = null;
            yield return IndependentLegs(ScenePath, true, "faseB_independentes_fisico.txt", r => res = r);
            var p = res.Split(':');
            Assert.LessOrEqual(float.Parse(p[1]), 0.605f, "velocidade real");
            Assert.LessOrEqual(float.Parse(p[2]), 0.001f, "penetração");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Referencia_Destinos_Independentes_Legado()
        {
            string res = null;
            yield return IndependentLegs("Assets/_Project/Scenes/CasaGrandeBairroScene.unity", false, "faseB_independentes_legado.txt", r => res = r);
        }
    }
}
#endif
