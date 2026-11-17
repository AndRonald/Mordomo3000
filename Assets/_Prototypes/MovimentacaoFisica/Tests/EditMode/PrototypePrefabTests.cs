using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MordomoProto.Fisica.Tests
{
    public class PrototypePrefabTests
    {
        const string PrefabPath = "Assets/_Prototypes/MovimentacaoFisica/Prefabs/Robo_Prototipo.prefab";

        GameObject _inst;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "prefab do protótipo");
            _inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_inst); }

        [Test]
        public void Collider_Original_Preservado()
        {
            var box = _inst.GetComponent<BoxCollider>();
            Assert.AreEqual(new Vector3(0.55f, 1.10f, 0.65f), box.size);
            Assert.AreEqual(new Vector3(0f, 0.55f, 0f), box.center);
            var rb = _inst.GetComponent<Rigidbody>();
            Assert.IsTrue(rb.isKinematic);
        }

        [Test]
        public void QuatroPivos_NasPosicoesAprovadas()
        {
            string[] names = { "Pivo_Roda_FE", "Pivo_Roda_FD", "Pivo_Roda_TE", "Pivo_Roda_TD" };
            float[] sx = { -1, 1, -1, 1 }, sz = { 1, 1, -1, -1 };
            for (int i = 0; i < 4; i++)
            {
                var t = _inst.transform.Find("Rodas/" + names[i]);
                Assert.IsNotNull(t, names[i]);
                Assert.AreEqual(sx[i] * 0.2416f, t.position.x, 1e-4f);
                Assert.AreEqual(0.14f, t.position.y, 1e-4f);          // eixo a 0,14 m
                Assert.AreEqual(sz[i] * 0.175f, t.position.z, 1e-4f); // ±0,175 m
            }
            var a = _inst.transform.Find("Rodas/Pivo_Roda_FD").position.x - _inst.transform.Find("Rodas/Pivo_Roda_FE").position.x;
            Assert.AreEqual(0.483f, a, 1e-3f);                        // bitola 0,483 m
        }

        [Test]
        public void Rodas_Circulares_D028_ApoiadasNoChao()
        {
            foreach (Transform piv in _inst.transform.Find("Rodas"))
            {
                var tyre = piv.Find("Pneu").GetComponent<MeshRenderer>();
                Assert.AreEqual(0.28f, tyre.bounds.size.y, 2e-3f);
                Assert.AreEqual(0.28f, tyre.bounds.size.z, 2e-3f);    // circular (altura = profundidade)
                Assert.AreEqual(0.060f, tyre.bounds.size.x, 2e-3f);
                Assert.AreEqual(0f, tyre.bounds.min.y, 1e-3f);        // base no piso
            }
        }

        [Test]
        public void Envelope_DasRodas_DentroDoColliderOriginal()
        {
            Bounds b = new Bounds(); bool first = true;
            foreach (var r in _inst.transform.Find("Rodas").GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Assert.LessOrEqual(b.max.x, 0.275f + 1e-3f);
            Assert.GreaterOrEqual(b.min.x, -0.275f - 1e-3f);
            Assert.LessOrEqual(b.max.z, 0.325f);
            Assert.GreaterOrEqual(b.min.z, -0.325f);
            Assert.GreaterOrEqual(b.min.y, -1e-3f);
        }

        [Test]
        public void RodasOriginais_Elipticas_Desativadas_36Partes()
        {
            int off = 0, on = 0;
            foreach (var mr in _inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.transform.IsChildOf(_inst.transform.Find("Rodas"))) continue;
                string n = mr.name;
                bool wheel = n.StartsWith("Pneu") || n.StartsWith("Cubo") || n.StartsWith("Parafuso") || n.StartsWith("Tampa_Cubo");
                if (wheel) { if (!mr.gameObject.activeSelf) off++; else on++; }
            }
            Assert.AreEqual(36, off);
            Assert.AreEqual(0, on);
        }

        [Test]
        public void FbxOriginal_NaoFoiModificado_EPrefabOriginalIntacto()
        {
            // o FBX continua com as 36 partes e as rodas elípticas (escala 1 no Unity)
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Robot_Entregador.fbx");
            int wheelParts = 0;
            foreach (var mr in fbx.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = mr.name;
                if (n.StartsWith("Pneu") || n.StartsWith("Cubo") || n.StartsWith("Parafuso") || n.StartsWith("Tampa_Cubo")) wheelParts++;
            }
            Assert.AreEqual(36, wheelParts);
        }

        [Test]
        public void ComponentesDoProtótipo_Conectados()
        {
            var body = _inst.GetComponent<KinematicDriveBody>();
            var sync = _inst.GetComponent<WheelVisualSync>();
            Assert.IsNotNull(body); Assert.IsNotNull(body.guard); Assert.IsNotNull(sync);
            Assert.AreSame(body, sync.body);
            Assert.IsNotNull(sync.frontLeft); Assert.IsNotNull(sync.frontRight);
            Assert.IsNotNull(sync.rearLeft); Assert.IsNotNull(sync.rearRight);
            Assert.AreEqual(0.14f, body.config.wheelRadius, 1e-6f);
            Assert.AreEqual(0.483f, body.config.track, 1e-6f);
            Assert.AreEqual(1f, body.config.chi, 1e-6f);
        }
    }
}
