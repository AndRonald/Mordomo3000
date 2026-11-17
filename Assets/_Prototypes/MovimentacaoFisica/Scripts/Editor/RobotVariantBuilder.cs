using UnityEditor;
using UnityEngine;

namespace MordomoProto.Fisica.Editor
{
    // Gera Robot_Cubo_Fisico.prefab como VARIANTE de Assets/_Project/Prefabs/Robot_Cubo.prefab (o original não é alterado).
    public static class RobotVariantBuilder
    {
        public const string OriginalPath = "Assets/_Project/Prefabs/Robot_Cubo.prefab";
        public const string VariantPath = PrototypeAssetBuilder.Root + "/Prefabs/Robot_Cubo_Fisico.prefab";

        public static string Build()
        {
            PrototypeAssetBuilder.BuildMaterials();
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPath);
            if (original == null) return "Robot_Cubo.prefab não encontrado";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(original);
            try
            {
                var visual = inst.transform.Find("Robot_Entregador");
                int disabled = 0;
                foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>(true))
                    if (PrototypeAssetBuilder.IsWheelPart(mr.name)) { mr.gameObject.SetActive(false); disabled++; }
                PrototypeAssetBuilder.ApplyBodyHeightReduction(visual);

                // Ponto_Carga acompanha a redução de 13% do corpo (mantém a encomenda sobre a bandeja)
                var carga = inst.transform.Find("Ponto_Carga");
                if (carga != null)
                {
                    float y0 = PrototypeAssetBuilder.BodyBaseY, k = PrototypeAssetBuilder.BodyHeightScale;
                    var p = carga.localPosition; p.y = y0 + (p.y - y0) * k; carga.localPosition = p;
                }

                var rodas = new GameObject("Rodas").transform;
                rodas.SetParent(inst.transform, false);
                Transform P(string nm, float sx, float sz)
                {
                    var tr = new GameObject(nm).transform;
                    tr.SetParent(rodas, false);
                    tr.localPosition = new Vector3(sx * PrototypeAssetBuilder.WheelX, PrototypeAssetBuilder.AxleHeight, sz * PrototypeAssetBuilder.WheelZ);
                    PrototypeAssetBuilder.BuildWheel(tr);
                    return tr;
                }
                var fl = P("Pivo_Roda_FE", -1, +1); var fr = P("Pivo_Roda_FD", +1, +1);
                var rl = P("Pivo_Roda_TE", -1, -1); var rr = P("Pivo_Roda_TD", +1, -1);

                var body = RobotRig.Configure(inst, new DriveConfig());
                var sync = inst.AddComponent<WheelVisualSync>();
                sync.body = body; sync.frontLeft = fl; sync.frontRight = fr; sync.rearLeft = rl; sync.rearRight = rr;
                var adapter = inst.AddComponent<PhysicalDriveAdapter>();
                var bridge = inst.AddComponent<LegacyControllerBridge>();
                inst.AddComponent<PhysicalRobotReset>();
                bridge.controller = inst.GetComponent<RobotNavigationController>();
                bridge.adapter = adapter;

                var saved = PrefabUtility.SaveAsPrefabAsset(inst, VariantPath);
                AssetDatabase.SaveAssets();
                return "Variante salva: " + (saved != null) + " | partes de roda desativadas: " + disabled;
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
