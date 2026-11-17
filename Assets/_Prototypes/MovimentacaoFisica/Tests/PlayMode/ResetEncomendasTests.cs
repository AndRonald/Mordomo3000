#if UNITY_EDITOR
using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MordomoProto.Fisica.Tests
{
    public class ResetEncomendasTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator Encomendas_EntregaCompleta_E_Limpeza()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Prototypes/MovimentacaoFisica/Scenes/CasaGrandeBairroScene_Fisica.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var log = new StringBuilder("== reset de encomendas (cópia física) ==\n");
            var mgr = Object.FindAnyObjectByType<PackageDeliveryManager>();
            var rst = Object.FindAnyObjectByType<DeliveryResetController>();
            log.AppendLine("PackageDeliveryManager=" + (mgr != null) + " DeliveryResetController=" + (rst != null) + (rst != null ? " enabled=" + rst.enabled + " ativo=" + rst.gameObject.activeInHierarchy : ""));
            Assert.NotNull(mgr);
            bool delivered = false; mgr.PackageDelivered += (id, dest, secs, cap) => { delivered = true; log.AppendLine("entregue: pacote " + id + " em " + dest + " (" + secs.ToString("F1") + " s)"); };
            Assert.IsTrue(mgr.CreatePackage());
            float t0 = Time.time;
            while (!delivered && Time.time - t0 < 120f) yield return null;
            Assert.IsTrue(delivered, "encomenda não foi entregue em 120 s");
            yield return null;
            int occ = 0, packs = 0;
            foreach (var d in Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include)) occ += d.OccupiedCount;
            packs = Object.FindObjectsByType<PackageItem>(FindObjectsInactive.Include).Length;
            log.AppendLine("antes da limpeza: slots ocupados=" + occ + " PackageItem na cena=" + packs);
            Assert.Greater(occ, 0);
            int removed = 0;
            foreach (var d in Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include)) removed += d.ClearAll();   // exatamente o que o R executa
            yield return null; yield return null;
            int occ2 = 0; foreach (var d in Object.FindObjectsByType<DeliveryDestination>(FindObjectsInactive.Include)) occ2 += d.OccupiedCount;
            log.AppendLine("ClearAll removeu " + removed + " | depois: slots ocupados=" + occ2 + " PackageItem na cena=" + Object.FindObjectsByType<PackageItem>(FindObjectsInactive.Include).Length);
            PlayResultsLog.Append("reset_encomendas.txt", log.ToString());
            Debug.Log(log.ToString());
            Assert.AreEqual(0, occ2, "slots continuam ocupados após ClearAll");
        }
    }
}
#endif
