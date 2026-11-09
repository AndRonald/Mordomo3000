using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the simulation speed through Time.timeScale (1x, 2x or paused) and draws a small HUD.
/// Keys: T toggles 1x/2x, Space pauses and resumes. The default speed is never persisted:
/// the time scale is reset to 1 when this component is disabled or destroyed.
/// </summary>
public sealed class SimulationSpeedController : MonoBehaviour
{
    public enum SpeedMode { Normal, Fast, Paused }

    [SerializeField, Min(1f)] private float fastScale = 2f;

    private SpeedMode mode = SpeedMode.Normal;
    private SpeedMode modeBeforePause = SpeedMode.Normal;

    public SpeedMode Mode => mode;
    public bool IsPaused => mode == SpeedMode.Paused;

    public string Label
    {
        get
        {
            switch (mode)
            {
                case SpeedMode.Fast: return fastScale.ToString("0.#") + "x";
                case SpeedMode.Paused: return "PAUSADO";
                default: return "1x";
            }
        }
    }

    private void OnEnable()
    {
        SetMode(SpeedMode.Normal);
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.tKey.wasPressedThisFrame)
        {
            ToggleFast();
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void SetNormal() { SetMode(SpeedMode.Normal); }

    public void SetFast() { SetMode(SpeedMode.Fast); }

    public void ToggleFast()
    {
        SetMode(mode == SpeedMode.Fast ? SpeedMode.Normal : SpeedMode.Fast);
    }

    public void TogglePause()
    {
        if (mode == SpeedMode.Paused)
        {
            SetMode(modeBeforePause);
        }
        else
        {
            modeBeforePause = mode;
            SetMode(SpeedMode.Paused);
        }
    }

    private void SetMode(SpeedMode newMode)
    {
        mode = newMode;
        switch (newMode)
        {
            case SpeedMode.Fast: Time.timeScale = fastScale; break;
            case SpeedMode.Paused: Time.timeScale = 0f; break;
            default: Time.timeScale = 1f; break;
        }
    }

    private void OnGUI()
    {
        var scale = Mathf.Max(1f, Screen.height / 720f);
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float x = 12f;
        const float y = 12f;
        GUI.Box(new Rect(x, y, 250f, 62f), GUIContent.none);

        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        style.normal.textColor = IsPaused ? new Color(1f, 0.55f, 0.2f) : (mode == SpeedMode.Fast ? new Color(1f, 0.9f, 0.3f) : Color.white);
        GUI.Label(new Rect(x + 8f, y + 2f, 234f, 24f), "Velocidade: " + Label, style);

        if (GUI.Button(new Rect(x + 8f, y + 30f, 70f, 24f), "1x")) { SetNormal(); }
        if (GUI.Button(new Rect(x + 84f, y + 30f, 70f, 24f), "2x")) { SetFast(); }
        if (GUI.Button(new Rect(x + 160f, y + 30f, 82f, 24f), IsPaused ? "Retomar" : "Pausar")) { TogglePause(); }

        GUI.matrix = matrix;
    }
}
