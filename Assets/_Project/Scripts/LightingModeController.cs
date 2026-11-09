using UnityEngine;

/// <summary>
/// Alterna a iluminação da cena entre Dia (estado inicial) e Noite, com um painel IMGUI.
/// Altera apenas configurações de iluminação (luz direcional, ambiente, skybox e grupos de luzes).
/// Não toca em robô, câmeras, sensores, NavMesh, física nem no Volume compartilhado.
/// Todo o estado original é capturado no Awake e restaurado em OnDisable/OnDestroy.
/// </summary>
[DisallowMultipleComponent]
public sealed class LightingModeController : MonoBehaviour
{
    public enum LightingMode
    {
        Day,
        Night
    }

    [Header("Luz principal")]
    [SerializeField] private Light sun;
    [SerializeField] private float nightSunIntensity = 0.06f;
    [SerializeField] private float nightSunTemperature = 12000f;

    [Header("Ambiente noturno")]
    [SerializeField] private Material nightSkybox;
    [SerializeField] private Color nightAmbientColor = new Color(0.045f, 0.055f, 0.09f, 1f);

    [Header("Grupos ativos somente à noite")]
    [SerializeField] private GameObject[] nightOnlyGroups;

    [Header("Painel")]
    [SerializeField] private bool startExpanded = true;

    private const float PanelWidth = 200f;
    private const float PanelMargin = 12f;
    private const float HeaderHeight = 30f;
    private const float RowHeight = 28f;

    private LightingMode mode = LightingMode.Day;
    private bool expanded;
    private bool captured;

    private float originalSunIntensity;
    private float originalSunTemperature;
    private Color originalSunColor;
    private bool originalSunUsesTemperature;
    private Material originalSkybox;
    private UnityEngine.Rendering.AmbientMode originalAmbientMode;
    private Color originalAmbientLight;
    private float originalAmbientIntensity;
    private bool[] originalGroupActive;

    private GUIStyle titleStyle;

    public LightingMode CurrentMode => mode;

    private void Awake()
    {
        expanded = startExpanded;
        CaptureOriginalState();
        ApplyMode(LightingMode.Day);
    }

    private void OnDisable()
    {
        RestoreOriginalState();
    }

    private void OnDestroy()
    {
        RestoreOriginalState();
    }

    public void SetMode(LightingMode newMode)
    {
        if (!captured)
        {
            CaptureOriginalState();
        }

        ApplyMode(newMode);
    }

    private void CaptureOriginalState()
    {
        if (captured)
        {
            return;
        }

        if (sun != null)
        {
            originalSunIntensity = sun.intensity;
            originalSunTemperature = sun.colorTemperature;
            originalSunColor = sun.color;
            originalSunUsesTemperature = sun.useColorTemperature;
        }

        originalSkybox = RenderSettings.skybox;
        originalAmbientMode = RenderSettings.ambientMode;
        originalAmbientLight = RenderSettings.ambientLight;
        originalAmbientIntensity = RenderSettings.ambientIntensity;

        int count = nightOnlyGroups != null ? nightOnlyGroups.Length : 0;
        originalGroupActive = new bool[count];
        for (int i = 0; i < count; i++)
        {
            originalGroupActive[i] = nightOnlyGroups[i] != null && nightOnlyGroups[i].activeSelf;
        }

        captured = true;
    }

    private void ApplyMode(LightingMode newMode)
    {
        mode = newMode;
        bool night = newMode == LightingMode.Night;

        if (sun != null)
        {
            if (night)
            {
                sun.useColorTemperature = true;
                sun.colorTemperature = nightSunTemperature;
                sun.intensity = nightSunIntensity;
            }
            else
            {
                sun.useColorTemperature = originalSunUsesTemperature;
                sun.colorTemperature = originalSunTemperature;
                sun.color = originalSunColor;
                sun.intensity = originalSunIntensity;
            }
        }

        if (night)
        {
            if (nightSkybox != null)
            {
                RenderSettings.skybox = nightSkybox;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = nightAmbientColor;
            RenderSettings.ambientIntensity = 1f;
        }
        else
        {
            RenderSettings.skybox = originalSkybox;
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbientLight;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
        }

        if (nightOnlyGroups != null)
        {
            for (int i = 0; i < nightOnlyGroups.Length; i++)
            {
                if (nightOnlyGroups[i] != null)
                {
                    nightOnlyGroups[i].SetActive(night);
                }
            }
        }

        DynamicGI.UpdateEnvironment();
    }

    private void RestoreOriginalState()
    {
        if (!captured)
        {
            return;
        }

        if (sun != null)
        {
            sun.useColorTemperature = originalSunUsesTemperature;
            sun.colorTemperature = originalSunTemperature;
            sun.color = originalSunColor;
            sun.intensity = originalSunIntensity;
        }

        RenderSettings.skybox = originalSkybox;
        RenderSettings.ambientMode = originalAmbientMode;
        RenderSettings.ambientLight = originalAmbientLight;
        RenderSettings.ambientIntensity = originalAmbientIntensity;

        if (nightOnlyGroups != null && originalGroupActive != null)
        {
            for (int i = 0; i < nightOnlyGroups.Length && i < originalGroupActive.Length; i++)
            {
                if (nightOnlyGroups[i] != null)
                {
                    nightOnlyGroups[i].SetActive(originalGroupActive[i]);
                }
            }
        }

        mode = LightingMode.Day;
        captured = false;
    }

    private void OnGUI()
    {
        float scale = Mathf.Max(1f, Screen.height / 720f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 16
            };
        }

        float virtualWidth = Screen.width / scale;
        float x = virtualWidth - PanelWidth - PanelMargin;
        float y = PanelMargin;
        float height = expanded ? HeaderHeight + RowHeight + 8f : HeaderHeight;

        GUI.Box(new Rect(x, y, PanelWidth, height), GUIContent.none);
        GUI.Label(new Rect(x + 8f, y + 4f, PanelWidth - 70f, 24f), "Iluminação: " + (mode == LightingMode.Day ? "Dia" : "Noite"), titleStyle);

        if (GUI.Button(new Rect(x + PanelWidth - 56f, y + 3f, 48f, 24f), expanded ? "Fechar" : "Abrir"))
        {
            expanded = !expanded;
        }

        if (expanded)
        {
            float rowY = y + HeaderHeight + 2f;
            bool wasEnabled = GUI.enabled;

            GUI.enabled = mode != LightingMode.Day;
            if (GUI.Button(new Rect(x + 8f, rowY, 88f, 24f), "Dia"))
            {
                ApplyMode(LightingMode.Day);
            }

            GUI.enabled = mode != LightingMode.Night;
            if (GUI.Button(new Rect(x + 104f, rowY, 88f, 24f), "Noite"))
            {
                ApplyMode(LightingMode.Night);
            }

            GUI.enabled = wasEnabled;
        }

        GUI.matrix = previousMatrix;
    }
}
