#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MordomoProto.Fisica.Tests
{
    // MEDIÇÃO SOMENTE LEITURA do controlador atual na CasaGrandeBairroScene (carregada em memória, nada é salvo).
    public class MedicaoControladorBairroTests
    {
        const string ScenePath = "Assets/_Project/Scenes/CasaGrandeBairroScene.unity";

        static object Get(object o, string f)
        {
            var fi = o.GetType().GetField(f, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return fi == null ? null : fi.GetValue(o);
        }

        struct S { public float t, x, z, yaw, real, av, dv, cmd; }

        [UnityTest, Timeout(600000)]
        public IEnumerator Medicao_Bairro()
        {
            var sb = new StringBuilder();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var ctrl = Object.FindFirstObjectByType<RobotNavigationController>();
            Assert.IsNotNull(ctrl, "controlador não encontrado");
            var agent = ctrl.GetComponent<NavMeshAgent>();
            sb.AppendLine("timeScale=" + Time.timeScale + " fixedDt=" + Time.fixedDeltaTime + " robô=" + ctrl.name + " pos=" + ctrl.transform.position.ToString("F3"));
            sb.AppendLine("CONFIG agent.speed=" + agent.speed + " agent.acceleration=" + agent.acceleration + " angularSpeed=" + agent.angularSpeed + " radius=" + agent.radius + " updatePosition=" + agent.updatePosition + " updateRotation=" + agent.updateRotation + " obstacleAvoidance=" + agent.obstacleAvoidanceType + " autoBraking=" + agent.autoBraking);
            foreach (var n in new[] { "forwardAcceleration", "brakingDeceleration", "turnInPlaceAngle", "fullSpeedHeadingAngle", "minimumTurningSpeedFraction" }) sb.AppendLine("CONFIG ctrl." + n + "=" + Get(ctrl, n));
            var dests = new Dictionary<string, Transform>();
            foreach (var n in new[] { "pontoColeta", "sala", "cozinha", "quarto", "escritorio" }) { var tr = Get(ctrl, n) as Transform; if (tr != null) dests[n] = tr; }
            sb.AppendLine("destinos: " + string.Join(", ", new List<string>(dests.Keys).ToArray()));
            Vector3 start = ctrl.transform.position; Quaternion startRot = ctrl.transform.rotation;

            foreach (var kv in dests)
            {
                if (kv.Key == "pontoColeta") continue;
                ctrl.transform.SetPositionAndRotation(start, startRot);
                if (agent.isOnNavMesh) { agent.Warp(start); agent.ResetPath(); }
                yield return null;
                bool ok = ctrl.MoveTo(kv.Value.position);
                var path = agent.path; var corners = path.corners;
                float plen = 0; for (int i = 1; i < corners.Length; i++) plen += Vector3.Distance(corners[i - 1], corners[i]);
                sb.AppendLine("== destino " + kv.Key + " MoveTo=" + ok + " cantos=" + corners.Length + " comprimento≈" + plen.ToString("F2") + "m");
                var L = new List<S>(); float t = 0; Vector3 prev = ctrl.transform.position; float prevYaw = ctrl.transform.eulerAngles.y;
                float maxT = 120f; float distAcc = 0; bool arrived = false;
                while (t < maxT)
                {
                    yield return null;
                    float dt = Time.deltaTime; if (dt <= 0) continue; t += dt;
                    var p = ctrl.transform.position; var yaw = ctrl.transform.eulerAngles.y;
                    float real = new Vector3(p.x - prev.x, 0, p.z - prev.z).magnitude / dt; distAcc += real * dt;
                    var cmdObj = Get(ctrl, "currentForwardSpeed"); float cmd = cmdObj is float ? (float)cmdObj : -1f;
                    L.Add(new S { t = t, x = p.x, z = p.z, yaw = Mathf.DeltaAngle(prevYaw, yaw) / dt, real = real, av = agent.velocity.magnitude, dv = agent.desiredVelocity.magnitude, cmd = cmd });
                    prev = p; prevYaw = yaw;
                    var st = Get(ctrl, "navigationState"); var hasDest = Get(ctrl, "hasActiveDestination");
                    if (hasDest is bool && !(bool)hasDest && t > 1f) { arrived = true; break; }
                }
                // estatísticas
                int nC = 0, nK = 0; double rC = 0, avC = 0, cmdC = 0, dvC = 0, rK = 0, yawK = 0, avK = 0; float maxReal = 0, maxAv = 0, maxCmd = 0;
                foreach (var s in L)
                {
                    if (s.real > maxReal) maxReal = s.real; if (s.av > maxAv) maxAv = s.av; if (s.cmd > maxCmd) maxCmd = s.cmd;
                    if (s.cmd >= 0.59f && Mathf.Abs(s.yaw) < 3f) { nC++; rC += s.real; avC += s.av; cmdC += s.cmd; dvC += s.dv; }
                    if (Mathf.Abs(s.yaw) > 15f && s.cmd > 0.05f) { nK++; rK += s.real; yawK += Mathf.Abs(s.yaw); avK += s.av; }
                }
                sb.AppendLine("frames=" + L.Count + " tempo=" + t.ToString("F1") + "s chegou=" + arrived + " distReal=" + distAcc.ToString("F2") + "m (caminho NavMesh " + plen.ToString("F2") + "m) velMédiaGlobal=" + (distAcc / Mathf.Max(t, 0.01f)).ToString("F3") + "m/s");
                sb.AppendLine("picos: real=" + maxReal.ToString("F3") + " agent.velocity=" + maxAv.ToString("F3") + " currentForwardSpeed(controlador)=" + maxCmd.ToString("F3"));
                if (nC > 0) sb.AppendLine("RETA (cmd>=0,59 e giro<3°/s) n=" + nC + ": real=" + (rC / nC).ToString("F3") + " agent.velocity=" + (avC / nC).ToString("F3") + " cmd=" + (cmdC / nC).ToString("F3") + " desiredVel=" + (dvC / nC).ToString("F3") + " razão real/cmd=" + ((rC / nC) / (cmdC / nC)).ToString("F2"));
                else sb.AppendLine("RETA: sem amostras");
                if (nK > 0) sb.AppendLine("CURVA (giro>15°/s) n=" + nK + ": real=" + (rK / nK).ToString("F3") + " agent.velocity=" + (avK / nK).ToString("F3") + " giro médio=" + (yawK / nK).ToString("F1") + "°/s");
                else sb.AppendLine("CURVA: sem amostras");
                var sbT = new StringBuilder("trilha 1/s: "); float nextT = 0; foreach (var s in L) if (s.t >= nextT) { sbT.Append("[" + s.t.ToString("F0") + "s ("+ s.x.ToString("F2") + "," + s.z.ToString("F2") + ") r=" + s.real.ToString("F2") + " av=" + s.av.ToString("F2") + " cmd=" + s.cmd.ToString("F2") + "] "); nextT += 3f; }
                sb.AppendLine(sbT.ToString());
                if (kv.Key == "sala") break; // um destino basta para a metodologia; os demais só se necessário
            }

            // Experimento isolado do mecanismo: agente de teste (não é o robô) com updatePosition=false
            ctrl.gameObject.SetActive(false);
            for (int variant = 0; variant < 2; variant++)
            {
                var go = new GameObject("medicao_agente"); go.transform.position = start;
                var ag = go.AddComponent<NavMeshAgent>();
                ag.speed = agent.speed; ag.acceleration = agent.acceleration; ag.angularSpeed = agent.angularSpeed; ag.radius = agent.radius; ag.height = agent.height;
                ag.autoBraking = agent.autoBraking; ag.obstacleAvoidanceType = agent.obstacleAvoidanceType; ag.areaMask = agent.areaMask;
                ag.updatePosition = false; ag.updateRotation = false; ag.Warp(start);
                Transform target = null; foreach (var kv in dests) if (kv.Key == "sala") target = kv.Value;
                ag.SetDestination(target.position);
                yield return null;
                float t = 0; var lastNext = ag.nextPosition; float cum = 0; var samples = new List<float>(); float vsum = 0; int vn = 0;
                float fwdSpeed = 0.6f;
                while (t < 6f)
                {
                    yield return null; float dt = Time.deltaTime; if (dt <= 0) continue; t += dt;
                    var dvel = ag.desiredVelocity; dvel.y = 0;
                    if (dvel.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.RotateTowards(go.transform.rotation, Quaternion.LookRotation(dvel.normalized), ag.angularSpeed * dt);
                    if (variant == 1) ag.Move(go.transform.forward * (fwdSpeed * dt));
                    go.transform.position = ag.nextPosition;
                    var nn = ag.nextPosition; float r = new Vector3(nn.x - lastNext.x, 0, nn.z - lastNext.z).magnitude / dt; lastNext = nn; cum += r * dt;
                    if (t > 2f) { vsum += r; vn++; }
                }
                sb.AppendLine("EXPERIMENTO agente isolado variante " + (variant == 0 ? "A: sem agent.Move (só simulação interna)" : "B: com agent.Move(forward*0,6*dt) a cada frame (como o controlador)") + ": velocidade média (t>2s)=" + (vsum / Mathf.Max(vn, 1)).ToString("F3") + "m/s | agent.velocity final=" + ag.velocity.magnitude.ToString("F3") + " | distância 6s=" + cum.ToString("F2") + "m");
                Object.Destroy(go); yield return null;
            }
            ctrl.gameObject.SetActive(true);
            System.IO.Directory.CreateDirectory("Temp/ProtoResults");
            System.IO.File.WriteAllText("Temp/ProtoResults/medicao_bairro.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
#endif
