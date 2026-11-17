using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MordomoProto.Fisica.Editor
{
    // Gera os assets ISOLADOS do protótipo (materiais, prefab do robô, cena). Não toca nenhum arquivo original.
    public static class PrototypeAssetBuilder
    {
        public const string Root = "Assets/_Prototypes/MovimentacaoFisica";
        public const string FbxPath = "Assets/_Project/Models/Robot_Entregador.fbx";
        public const string PrefabPath = Root + "/Prefabs/Robo_Prototipo.prefab";
        public const string ScenePath = Root + "/Scenes/PrototipoFisica_Circuito.unity";

        // Rodas (decisão aprovada, opção D)
        public const float WheelDiameter = 0.28f;
        public const float WheelRadius = 0.14f;
        public const float WheelWidth = 0.060f;
        public const float AxleHeight = 0.14f;
        public const float WheelX = 0.2416f;     // bitola 0,483 m
        public const float WheelZ = 0.175f;      // centros longitudinais

        // Redução visual da altura do corpo (aprovada): escala Y absoluta a partir da base do corpo.
        // Valores ABSOLUTOS sobre o FBX recém-instanciado => idempotente (rodar N vezes dá o mesmo prefab).
        public const float BodyHeightScale = 0.87f;
        public const float BodyBaseY = 0.066f;          // base do Protetor_Inferior
        const float AntennaBaseTopY = 0.535f;           // topo original de Antena_Base
        const float AntennaStalkTopY = 1.086f;          // topo de Antena_Haste (mantido)

        static readonly string[] WheelPartPrefixes = { "Pneu", "Cubo", "Parafuso", "Tampa_Cubo" };

        static Material Mat(string name, Color c, float metallic, float smooth)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials()
        {
            System.IO.Directory.CreateDirectory(Root + "/Materials");
            Mat("Proto_Pneu", new Color(0.04f, 0.04f, 0.045f), 0f, 0.25f);
            Mat("Proto_Aro", new Color(0.72f, 0.73f, 0.75f), 0.85f, 0.55f);
            Mat("Proto_Parafuso", new Color(0.30f, 0.30f, 0.32f), 0.9f, 0.4f);
            Mat("Proto_Parede", new Color(0.82f, 0.80f, 0.76f), 0f, 0.15f);
            Mat("Proto_Piso", new Color(0.46f, 0.46f, 0.48f), 0f, 0.2f);
            Mat("Proto_Obstaculo", new Color(0.95f, 0.45f, 0.1f), 0f, 0.3f);
            AssetDatabase.SaveAssets();
        }

        internal static Material M(string n) { return AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + n + ".mat"); }

        static GameObject Cyl(string name, Transform parent, Vector3 localPos, float dia, float width, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);       // eixo do cilindro (Y) -> X
            g.transform.localScale = new Vector3(dia, width * 0.5f, dia);     // cilindro padrão: altura 2, diâmetro 1
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        // Roda circular Ø0,28 m: pneu, aro, tampa do cubo e 6 parafusos (proporções da roda original, escala 0,875).
        internal static void BuildWheel(Transform pivot)
        {
            float k = WheelDiameter / 0.32f;
            Cyl("Pneu", pivot, Vector3.zero, WheelDiameter, WheelWidth, M("Proto_Pneu"));
            Cyl("Aro", pivot, Vector3.zero, 0.19f * k, WheelWidth + 0.004f, M("Proto_Aro"));
            Cyl("Tampa_Cubo", pivot, Vector3.zero, 0.07f * k, 0.0669f, M("Proto_Aro"));
            float r = 0.062f * k;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Cyl("Parafuso_" + i, pivot, new Vector3(0f, r * Mathf.Cos(a), r * Mathf.Sin(a)), 0.024f * k, 0.066f, M("Proto_Parafuso"));
            }
        }

        internal static bool IsWheelPart(string n)
        {
            foreach (var p in WheelPartPrefixes) if (n.StartsWith(p)) return true;
            return false;
        }

        // Escala o corpo em Y (sem mexer em X/Z), mantém a base em BodyBaseY, a Antena_Ponta intacta
        // e estica a Antena_Haste para continuar chegando ao mesmo topo. Rodas originais ignoradas.
        public static void ApplyBodyHeightReduction(Transform visual)
        {
            // 'visual' já é a raiz Robot_Entregador da instância do FBX (filhos = peças).
            Transform body = visual.name.StartsWith("Robot_Entregador") ? visual : visual.Find("Robot_Entregador");
            if (body == null) return;
            float k = BodyHeightScale, y0 = BodyBaseY;
            foreach (Transform t in body)
            {
                string n = t.name;
                if (n.StartsWith("Antena_Ponta") || IsWheelPart(n)) continue;
                if (n.StartsWith("Antena_Haste"))
                {
                    float newBottom = y0 + (AntennaBaseTopY - y0) * k;
                    float sy = (AntennaStalkTopY - newBottom) / (AntennaStalkTopY - AntennaBaseTopY);
                    t.localScale = new Vector3(1f, sy, 1f);
                    t.localPosition = new Vector3(0f, newBottom - AntennaBaseTopY * sy, 0f);
                    continue;
                }
                t.localScale = new Vector3(1f, k, 1f);
                t.localPosition = new Vector3(0f, y0 * (1f - k), 0f);
            }
        }

        public static string BuildRobotPrefab()
        {
            BuildMaterials();
            System.IO.Directory.CreateDirectory(Root + "/Prefabs");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) return "FBX não encontrado";

            var root = new GameObject("Robo_Prototipo");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // igual ao Robot_Cubo original

            int disabled = 0;
            foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsWheelPart(mr.name)) { mr.gameObject.SetActive(false); disabled++; }
            }

            ApplyBodyHeightReduction(visual.transform);

            var rodas = new GameObject("Rodas").transform;
            rodas.SetParent(root.transform, false);
            Transform P(string n, float sx, float sz)
            {
                var t = new GameObject(n).transform;
                t.SetParent(rodas, false);
                t.localPosition = new Vector3(sx * WheelX, AxleHeight, sz * WheelZ);
                BuildWheel(t);
                return t;
            }
            var fl = P("Pivo_Roda_FE", -1, +1);
            var fr = P("Pivo_Roda_FD", +1, +1);
            var rl = P("Pivo_Roda_TE", -1, -1);
            var rr = P("Pivo_Roda_TD", +1, -1);

            var body = RobotRig.Configure(root, new DriveConfig());
            var sync = root.AddComponent<WheelVisualSync>();
            sync.body = body;
            sync.frontLeft = fl; sync.frontRight = fr; sync.rearLeft = rl; sync.rearRight = rr;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return "Prefab salvo: " + (saved != null) + " | partes de roda originais desativadas: " + disabled;
        }

        public static string BuildScene()
        {
            BuildMaterials();
            System.IO.Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Luz_Direcional");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGo = new GameObject("Camera_Topo");
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(3.2f, 11f, 3.4f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var circuits = new GameObject("Circuitos").transform;
            var wall = M("Proto_Parede"); var floor = M("Proto_Piso"); var obs = M("Proto_Obstaculo");
            var main = CircuitBuilder.Build(circuits, Vector3.zero, 1.20f, true, wall, floor, obs, "Circuito_Principal_1.20");
            var narrow = CircuitBuilder.Build(circuits, new Vector3(0f, 0f, -20f), 0.90f, false, wall, floor, obs, "Circuito_Restrito_0.90");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            robot.transform.SetPositionAndRotation(main.StartPosition, Quaternion.identity);

            var ctl = new GameObject("Cenario_Demo");
            var runner = ctl.AddComponent<CircuitRunner>();
            var demo = ctl.AddComponent<CircuitDemo>();
            runner.body = robot.GetComponent<KinematicDriveBody>();
            demo.body = runner.body;
            demo.runner = runner;
            demo.circuitOrigin = Vector3.zero;
            demo.corridorWidth = 1.20f;
            demo.withObstacle = true;
            demo.autoStart = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            return "Cena salva: " + ScenePath;
        }

        // ---------- conferência de interferências ----------
        static List<Vector3> SampleSurface(MeshFilter mf, float spacing)
        {
            var pts = new List<Vector3>();
            var mesh = mf.sharedMesh;
            var v = mesh.vertices; var tr = mesh.triangles;
            var tf = mf.transform;
            for (int i = 0; i < tr.Length; i += 3)
            {
                Vector3 a = tf.TransformPoint(v[tr[i]]), b = tf.TransformPoint(v[tr[i + 1]]), c = tf.TransformPoint(v[tr[i + 2]]);
                float e = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(e / spacing), 1, 40);
                for (int u = 0; u <= n; u++)
                    for (int w = 0; w <= n - u; w++)
                    {
                        float fu = u / (float)n, fw = w / (float)n;
                        pts.Add(a + (b - a) * fu + (c - a) * fw);
                    }
            }
            return pts;
        }

        public static string InterferenceReport()
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return "prefab ausente";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var box = inst.GetComponent<BoxCollider>();
                sb.AppendLine("Collider: size=" + box.size.ToString("F3") + " center=" + box.center.ToString("F3"));

                // envelope das rodas
                Bounds wb = new Bounds(); bool first = true;
                foreach (var r in inst.transform.Find("Rodas").GetComponentsInChildren<MeshRenderer>())
                {
                    if (first) { wb = r.bounds; first = false; } else wb.Encapsulate(r.bounds);
                }
                sb.AppendLine("Envelope das rodas: x[" + wb.min.x.ToString("F4") + "," + wb.max.x.ToString("F4") + "] y[" + wb.min.y.ToString("F4") + "," + wb.max.y.ToString("F4") + "] z[" + wb.min.z.ToString("F4") + "," + wb.max.z.ToString("F4") + "]");
                sb.AppendLine("Dentro do collider (x ±0,275 / z ±0,325 / y 0..1,10)? x:" + (wb.max.x <= 0.275f + 1e-4f && wb.min.x >= -0.275f - 1e-4f)
                    + " z:" + (wb.max.z <= 0.325f && wb.min.z >= -0.325f) + " y:" + (wb.min.y >= -1e-4f));

                // envelope do corpo visível (sem rodas)
                Bounds bb = new Bounds(); first = true;
                var bodyFilters = new List<MeshFilter>();
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.transform.IsChildOf(inst.transform.Find("Rodas"))) continue;
                    bodyFilters.Add(mf);
                    var r = mf.GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    if (first) { bb = r.bounds; first = false; } else bb.Encapsulate(r.bounds);
                }
                sb.AppendLine("Corpo (sem rodas): x[" + bb.min.x.ToString("F4") + "," + bb.max.x.ToString("F4") + "] z[" + bb.min.z.ToString("F4") + "," + bb.max.z.ToString("F4") + "] y[" + bb.min.y.ToString("F4") + "," + bb.max.y.ToString("F4") + "]");

                // pivôs
                var pivots = new List<Transform>();
                foreach (Transform t in inst.transform.Find("Rodas")) pivots.Add(t);
                foreach (var t in pivots) sb.AppendLine("Pivô " + t.name + " local=" + t.localPosition.ToString("F4") + " mundo=" + t.position.ToString("F4"));
                sb.AppendLine("Bitola entre centros: " + (pivots[1].position.x - pivots[0].position.x).ToString("F4") + " m | distância entre eixos: " + (pivots[0].position.z - pivots[2].position.z).ToString("F4") + " m");
                sb.AppendLine("Folga entre pneus do mesmo lado: " + ((pivots[0].position.z - pivots[2].position.z) - WheelDiameter).ToString("F4") + " m | base do pneu em y=" + (AxleHeight - WheelRadius).ToString("F4"));

                // interferência corpo x pneu (amostragem de superfície, 4 mm)
                var results = new SortedDictionary<string, string>();
                foreach (var mf in bodyFilters)
                {
                    if (mf.sharedMesh == null) continue;
                    var pts = SampleSurface(mf, 0.004f);
                    int inside = 0; float maxDepth = 0f;
                    foreach (var p in pts)
                        foreach (var pv in pivots)
                        {
                            Vector3 d = p - pv.position;
                            float rho = Mathf.Sqrt(d.y * d.y + d.z * d.z);
                            if (Mathf.Abs(d.x) < WheelWidth * 0.5f && rho < WheelRadius)
                            {
                                inside++;
                                float depth = Mathf.Min(WheelRadius - rho, WheelWidth * 0.5f - Mathf.Abs(d.x));
                                if (depth > maxDepth) maxDepth = depth;
                            }
                        }
                    if (inside > 0) results[mf.name] = inside + " pts (prof. máx " + (maxDepth * 1000f).ToString("F1") + " mm)";
                }
                sb.AppendLine("Superfícies do corpo DENTRO do volume dos pneus Ø0,28:");
                if (results.Count == 0) sb.AppendLine("  (nenhuma)");
                foreach (var kv in results) sb.AppendLine("  " + kv.Key + ": " + kv.Value);

                // mesma análise para o pneu ORIGINAL elíptico (referência)
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
                var origInst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                origInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                var resultsOrig = new SortedDictionary<string, string>();
                var origCenters = new List<Vector3>();
                foreach (var mf in origInst.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name.StartsWith("Pneu")) origCenters.Add(mf.GetComponent<MeshRenderer>().bounds.center);
                foreach (var mf in origInst.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (IsWheelPart(mf.name) || mf.sharedMesh == null) continue;
                    var pts = SampleSurface(mf, 0.004f);
                    int inside = 0; float maxDepth = 0f;
                    foreach (var p in pts)
                        foreach (var c in origCenters)
                        {
                            Vector3 d = p - c;
                            float hw = 0.0604f * 0.5f;
                            float e = (d.z / 0.065f) * (d.z / 0.065f) + (d.y / 0.1764f) * (d.y / 0.1764f);
                            if (Mathf.Abs(d.x) < hw && e < 1f)
                            {
                                inside++;
                                float depth = hw - Mathf.Abs(d.x);
                                if (depth > maxDepth) maxDepth = depth;
                            }
                        }
                    if (inside > 0) resultsOrig[mf.name] = inside + " pts (prof. lateral máx " + (maxDepth * 1000f).ToString("F1") + " mm)";
                }
                Object.DestroyImmediate(origInst);
                sb.AppendLine("Referência — superfícies do corpo dentro do pneu ORIGINAL elíptico:");
                if (resultsOrig.Count == 0) sb.AppendLine("  (nenhuma)");
                foreach (var kv in resultsOrig) sb.AppendLine("  " + kv.Key + ": " + kv.Value);

                // peças dentro da roda: dimensões e sobreposição interna
                var w0 = pivots[0];
                foreach (Transform part in w0)
                {
                    var r = part.GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    sb.AppendLine("  Roda FE / " + part.name + ": extensão x[" + (r.bounds.min.x - w0.position.x).ToString("F4") + "," + (r.bounds.max.x - w0.position.x).ToString("F4") + "] raio=" + (Mathf.Max(r.bounds.extents.y, r.bounds.extents.z)).ToString("F4"));
                }
            }
            finally { Object.DestroyImmediate(inst); }
            return sb.ToString();
        }
    }
}
