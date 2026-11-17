#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    // Fase S: ataques ao PROPRIO protótipo físico (entradas inválidas, adulteração de estado e de configuração).
    // Cada teste é um ataque; passar = o sistema se defende (estado finito, limites respeitados, sem movimento indevido).
    // Modelo de ameaça: quem chama a API pública (comando, teleporte, reset, config, destino) ou escreve no Transform pode estar errado ou hostil.
    public class FaseSSegurancaTests
    {
        const string ScenePath = "Assets/_Prototypes/MovimentacaoFisica/Scenes/CasaGrandeBairroScene_Fisica.unity";
        const float Eps = 1e-3f;

        static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
        static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        static bool Finite(double d) { return !double.IsNaN(d) && !double.IsInfinity(d); }

        static KinematicDriveBody NovoCorpo(out GameObject go)
        {
            go = new GameObject("AtkBody");
            go.AddComponent<Rigidbody>();
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.5f, 0.4f, 0.6f);
            return go.AddComponent<KinematicDriveBody>();   // sem guard: isola o corpo/modelo
        }

        static IEnumerator Passos(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        // ---------- A1: comando NaN no modelo puro ----------
        [Test]
        public void A1_Modelo_ComandoNaN_NaoCorrompePose()
        {
            var m = new DifferentialDriveModel(new DriveConfig().ToParameters());
            m.SetPose(new Pose2D(1, 2, 0.3));
            m.Step(double.NaN, double.NaN, 0.02);
            Assert.IsTrue(Finite(m.Pose.X) && Finite(m.Pose.Z) && Finite(m.Pose.Yaw), "pose corrompida por comando NaN: " + m.Pose.X + "," + m.Pose.Z + "," + m.Pose.Yaw);
            m.Step(0.5, 0.0, 0.02);
            Assert.IsTrue(Finite(m.Pose.X) && Finite(m.Pose.Z), "pose não se recupera depois de um comando válido");
        }

        // ---------- A2: comando infinito / gigante ----------
        [Test]
        public void A2_Modelo_ComandoInfinitoOuGigante_RespeitaLimites()
        {
            var p = new DriveConfig().ToParameters();
            var m = new DifferentialDriveModel(p);
            for (int i = 0; i < 200; i++) m.Step(double.PositiveInfinity, double.NegativeInfinity, 0.02);
            Assert.IsTrue(Finite(m.Pose.X) && Finite(m.Pose.Z), "pose não finita");
            Assert.LessOrEqual(System.Math.Abs(m.LinearSpeed), p.MaxSpeed + 1e-6, "excedeu velocidade máxima");
            m = new DifferentialDriveModel(p);
            for (int i = 0; i < 200; i++) m.Step(1e30, 1e30, 0.02);
            Assert.LessOrEqual(System.Math.Abs(m.LinearSpeed), p.MaxSpeed + 1e-6, "excedeu velocidade máxima com valor gigante");
        }

        // ---------- A3: configuração inválida/adulterada ----------
        [Test]
        public void A3_Config_Invalida_NaoGeraNaNNemInfinito()
        {
            GameObject go; var body = NovoCorpo(out go);
            try
            {
                var bad = new DriveConfig { track = 0f, chi = 0f, wheelRadius = 0f, maxSpeed = -1f, acceleration = -5f, braking = 0f };
                body.ApplyConfig(bad);
                var m = body.Model;
                m.Step(0.5, 0.2, 0.02);
                bool ok = Finite(m.Pose.X) && Finite(m.Pose.Z) && Finite(m.Pose.Yaw) && Finite(m.LeftAngle) && Finite(m.YawRate);
                Assert.IsTrue(ok, "config inválida aceita e gerou estado não finito: pose=" + m.Pose.X + "," + m.Pose.Z + " yaw=" + m.Pose.Yaw + " yawRate=" + m.YawRate + " angL=" + m.LeftAngle);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------- A4: comando NaN no corpo físico (Rigidbody) ----------
        [UnityTest]
        public IEnumerator A4_Corpo_ComandoNaN_RigidbodyContinuaValido()
        {
            GameObject go; var body = NovoCorpo(out go);
            yield return Passos(3);
            LogAssert.ignoreFailingMessages = true;
            body.SetCommand(float.NaN, float.NaN);
            yield return Passos(10);
            bool okPose = Finite(body.Body.position) && Finite(body.Pose.X) && Finite(body.Pose.Z);
            body.SetCommand(0.3f, 0f);
            yield return Passos(10);
            bool okRec = Finite(body.Body.position) && Finite(body.Pose.X);
            LogAssert.ignoreFailingMessages = false;
            Object.Destroy(go);
            Assert.IsTrue(okPose, "Rigidbody/pose corrompidos por comando NaN");
            Assert.IsTrue(okRec, "corpo não se recupera depois do NaN");
        }

        // ---------- A5: teleporte com posição inválida ----------
        [UnityTest]
        public IEnumerator A5_Corpo_TeleporteNaN_Rejeitado()
        {
            GameObject go; var body = NovoCorpo(out go);
            yield return Passos(3);
            Vector3 antes = body.Body.position;
            LogAssert.ignoreFailingMessages = true;
            body.Teleport(new Vector3(float.NaN, 0f, float.PositiveInfinity), float.NaN);
            yield return Passos(5);
            bool ok = Finite(body.Body.position) && Finite(body.Pose.X) && Finite(body.Pose.Z) && Finite(body.Pose.Yaw);
            LogAssert.ignoreFailingMessages = false;
            Vector3 depois = body.Body.position;
            Object.Destroy(go);
            Assert.IsTrue(ok, "teleporte inválido corrompeu o corpo (antes=" + antes + ", depois=" + depois + ")");
        }

        // ---------- A6: spam de comandos (desempenho e limites) ----------
        [UnityTest]
        public IEnumerator A6_Corpo_SpamDeComandos_ContinuaDentroDosLimites()
        {
            GameObject go; var body = NovoCorpo(out go);
            yield return Passos(3);
            float vmax = body.config.maxSpeed;
            float maxVisto = 0f;
            Vector3 prev = body.Body.position;
            for (int i = 0; i < 100; i++)
            {
                for (int k = 0; k < 2000; k++) body.SetCommand(Random.Range(-100f, 100f), Random.Range(-100f, 100f));
                yield return new WaitForFixedUpdate();
                Vector3 p = body.Body.position;
                float sp = new Vector2(p.x - prev.x, p.z - prev.z).magnitude / Time.fixedDeltaTime;
                if (sp > maxVisto) maxVisto = sp;
                prev = p;
            }
            double media = body.AverageStepMicroseconds;
            Object.Destroy(go);
            Assert.LessOrEqual(maxVisto, vmax + 0.05f, "velocidade acima do limite sob spam: " + maxVisto);
            Assert.Less(media, 1000.0, "custo de passo >= 1 ms sob spam: " + media + " us");
        }

        // ---------- cena do bairro: adaptador ----------
        sealed class Run { public PhysicalDriveAdapter ad; public KinematicDriveBody body; public NavMeshAgent agent; public RobotNavigationController ctl; }

        static IEnumerator Load(Run r)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            r.ctl = Object.FindAnyObjectByType<RobotNavigationController>();
            Assert.NotNull(r.ctl, "controlador");
            r.ad = r.ctl.GetComponent<PhysicalDriveAdapter>();
            r.body = r.ctl.GetComponent<KinematicDriveBody>();
            r.agent = r.ctl.GetComponent<NavMeshAgent>();
            r.ctl.SetKeyboardShortcutsEnabled(false);
            float t0 = Time.time;
            while (!r.agent.isOnNavMesh && Time.time - t0 < 3f) yield return null;
            Assert.IsTrue(r.agent.isOnNavMesh, "agente fora do NavMesh");
        }

        // ---------- A7: destino inválido no adaptador ----------
        [UnityTest, Timeout(120000)]
        public IEnumerator A7_Adaptador_DestinoInvalido_NaoMoveNemCorrompe()
        {
            var r = new Run(); yield return Load(r);
            r.ctl.enabled = false;   // isola: só o adaptador recebe o ataque
            r.ad.ExternalNavigating = null;
            Vector3 start = r.body.Position;
            LogAssert.ignoreFailingMessages = true;
            r.ad.SetDestination(new Vector3(float.NaN, 0f, float.NaN));
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            r.ad.SetDestination(new Vector3(float.PositiveInfinity, 0f, 0f));
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            LogAssert.ignoreFailingMessages = false;
            Vector3 end = r.body.Position;
            Assert.IsTrue(Finite(end) && Finite(r.body.Pose.Yaw), "corpo corrompido por destino inválido: " + end);
            float moved = new Vector2(end.x - start.x, end.z - start.z).magnitude;
            Assert.Less(moved, 0.05f, "robô se moveu " + moved + " m com destino inválido (estado=" + r.ad.State + ")");
        }

        // ---------- A8: reset do adaptador com pose inválida ----------
        [UnityTest, Timeout(120000)]
        public IEnumerator A8_Adaptador_HardResetNaN_Rejeitado()
        {
            var r = new Run(); yield return Load(r);
            r.ctl.enabled = false;
            LogAssert.ignoreFailingMessages = true;
            r.ad.HardReset(new Vector3(float.NaN, 0f, float.NaN), float.NaN);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            LogAssert.ignoreFailingMessages = false;
            Vector3 p = r.body.Body.position;
            Assert.IsTrue(Finite(p) && Finite(r.body.Pose.X) && Finite(r.body.transform.position), "HardReset(NaN) corrompeu corpo/transform: " + p);
        }

        // ---------- A9: adulteração do Transform por fora (teleporte de 10 m) ----------
        [UnityTest, Timeout(120000)]
        public IEnumerator A9_Adaptador_AdulteracaoDeTransform_Desfeita()
        {
            var r = new Run(); yield return Load(r);
            Vector3 fisico = r.body.Body.position;
            yield return null;   // fase Update, como no teste de interferência da Fase B
            r.ctl.transform.position = fisico + new Vector3(10f, 0f, 0f);
            r.ctl.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * r.ctl.transform.rotation;
            yield return null; yield return null;
            float d = new Vector2(r.ctl.transform.position.x - r.body.Position.x, r.ctl.transform.position.z - r.body.Position.z).magnitude;
            Assert.Less(d, 0.05f, "adulteração de 10 m não desfeita: divergência " + d + " m");
            Assert.Greater(r.ad.InterferenceFrames, 0, "interferência não registrada");
        }
    }
}
#endif
