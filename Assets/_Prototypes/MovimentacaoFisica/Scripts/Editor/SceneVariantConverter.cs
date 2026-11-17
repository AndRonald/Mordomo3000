using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace MordomoProto.Fisica.Editor
{
    // Troca o robô da CÓPIA da cena (CasaGrandeBairroScene_Fisica) pela variante física. Só grava a cena copia.
    public static class SceneVariantConverter
    {
        public const string CopyPath = PrototypeAssetBuilder.Root + "/Scenes/CasaGrandeBairroScene_Fisica.unity";

        static void CopyAll(Object from, Object to)
        {
            var a = new SerializedObject(from); var b = new SerializedObject(to);
            var p = a.GetIterator();
            bool en = p.NextVisible(true);
            while (en) { if (p.propertyPath != "m_Script") b.CopyFromSerializedProperty(p); en = p.NextVisible(false); }
            b.ApplyModifiedProperties();
        }

        public static string Convert()
        {
            var sb = new StringBuilder();
            if (!CopyPath.EndsWith("_Fisica.unity")) return "caminho inesperado";
            var scene = EditorSceneManager.OpenScene(CopyPath, OpenSceneMode.Single);
            var oldCtl = Object.FindAnyObjectByType<RobotNavigationController>();
            if (oldCtl == null) return "sem controlador";
            var oldGo = oldCtl.gameObject;
            if (oldGo.GetComponent<PhysicalDriveAdapter>() != null) return "já convertida";
            var parent = oldGo.transform.parent;
            var pos = oldGo.transform.position; var rot = oldGo.transform.rotation; int sib = oldGo.transform.GetSiblingIndex();

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(RobotVariantBuilder.VariantPath);
            var ng = (GameObject)PrefabUtility.InstantiatePrefab(variant, scene);
            ng.transform.SetParent(parent, false);
            ng.transform.SetPositionAndRotation(pos, rot);
            ng.transform.SetSiblingIndex(sib);

            var newCtl = ng.GetComponent<RobotNavigationController>();
            CopyAll(oldCtl, newCtl);
            CopyAll(oldGo.GetComponent<RobotSensorSuite>(), ng.GetComponent<RobotSensorSuite>());
            CopyAll(oldGo.GetComponent<NavMeshAgent>(), ng.GetComponent<NavMeshAgent>());
            ng.name = oldGo.name;

            int refs = 0;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.transform.IsChildOf(ng.transform) || mb.transform.IsChildOf(oldGo.transform)) continue;
                var so = new SerializedObject(mb);
                var p = so.GetIterator();
                bool en = p.NextVisible(true);
                while (en)
                {
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null)
                    {
                        var o = p.objectReferenceValue;
                        if (o == oldCtl) { p.objectReferenceValue = newCtl; refs++; sb.AppendLine("ref controlador: " + mb.name + "." + mb.GetType().Name + "." + p.propertyPath); }
                        else if (o == oldGo) { p.objectReferenceValue = ng; refs++; sb.AppendLine("ref GameObject: " + mb.name + "." + p.propertyPath); }
                        else if (o == oldGo.transform) { p.objectReferenceValue = ng.transform; refs++; sb.AppendLine("ref Transform: " + mb.name + "." + p.propertyPath); }
                        else if (o is Component c && c.transform.IsChildOf(oldGo.transform)) sb.AppendLine("REF DE FILHO NAO TRATADA: " + mb.name + "." + p.propertyPath + " -> " + c.name);
                    }
                    en = p.NextVisible(true);
                }
                so.ApplyModifiedProperties();
            }
            Object.DestroyImmediate(oldGo);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            sb.AppendLine("salva=" + saved + " refs religadas=" + refs + " pos=" + pos.ToString("F2"));
            return sb.ToString();
        }
    }
}
