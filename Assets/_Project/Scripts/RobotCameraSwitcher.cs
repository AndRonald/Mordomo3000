using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Switches between the general camera (the scene's existing Main Camera, left untouched) and a smooth
/// third-person camera that follows the robot. Key: C. The robot camera uses unscaled time so it stays
/// smooth at 2x and while the simulation is paused.
/// </summary>
public sealed class RobotCameraSwitcher : MonoBehaviour
{
    [SerializeField] private Camera generalCamera;
    [SerializeField] private Camera robotCamera;
    [SerializeField] private Transform robot;

    [Header("Follow")]
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 0.35f, 0f);
    [SerializeField] private float lookAhead = 1.4f;
    [SerializeField] private float positionSmoothTime = 0.25f;
    [SerializeField] private float yawSmoothTime = 0.35f;

    [Header("Occlusion presets (distance behind robot, height), tried from far to near")]
    [SerializeField] private Vector2[] presets =
    {
        new Vector2(3.0f, 3.3f),
        new Vector2(2.4f, 3.0f),
        new Vector2(1.8f, 2.8f),
        new Vector2(1.2f, 2.6f)
    };

    private bool robotView;
    private float smoothYaw;
    private float yawVelocity;
    private Vector3 positionVelocity;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    public bool IsRobotView => robotView;

    private void Awake()
    {
        if (generalCamera == null) { generalCamera = Camera.main; }
        Apply();
    }

    private void OnDisable()
    {
        robotView = false;
        Apply();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        SetRobotView(!robotView);
    }

    public void SetRobotView(bool value)
    {
        robotView = value && robot != null && robotCamera != null;
        if (robotView)
        {
            smoothYaw = robot.eulerAngles.y;
            yawVelocity = 0f;
            positionVelocity = Vector3.zero;
            SnapToIdeal();
        }

        Apply();
    }

    private void Apply()
    {
        if (generalCamera != null) { generalCamera.enabled = !robotView; }
        if (robotCamera != null) { robotCamera.enabled = robotView; }
    }

    private void LateUpdate()
    {
        if (!robotView || robot == null)
        {
            return;
        }

        var dt = Time.unscaledDeltaTime;
        smoothYaw = Mathf.SmoothDampAngle(smoothYaw, robot.eulerAngles.y, ref yawVelocity, yawSmoothTime, Mathf.Infinity, dt);
        var target = ComputeTarget(out var look);
        var cam = robotCamera.transform;
        cam.position = Vector3.SmoothDamp(cam.position, target, ref positionVelocity, positionSmoothTime, Mathf.Infinity, dt);
        var wanted = Quaternion.LookRotation(look - cam.position, Vector3.up);
        cam.rotation = Quaternion.Slerp(cam.rotation, wanted, 1f - Mathf.Exp(-8f * dt));
    }

    private void SnapToIdeal()
    {
        var target = ComputeTarget(out var look);
        robotCamera.transform.position = target;
        robotCamera.transform.rotation = Quaternion.LookRotation(look - target, Vector3.up);
    }

    private Vector3 ComputeTarget(out Vector3 look)
    {
        var heading = Quaternion.Euler(0f, smoothYaw, 0f);
        var pivot = robot.position + pivotOffset;
        look = pivot + heading * Vector3.forward * lookAhead;

        var fallback = pivot + heading * new Vector3(0f, presets[presets.Length - 1].y, -presets[presets.Length - 1].x);
        for (var i = 0; i < presets.Length; i++)
        {
            var candidate = pivot + heading * new Vector3(0f, presets[i].y, -presets[i].x);
            if (!IsBlocked(pivot, candidate))
            {
                return candidate;
            }
        }

        return fallback;
    }

    private bool IsBlocked(Vector3 from, Vector3 to)
    {
        var direction = to - from;
        var distance = direction.magnitude;
        var count = Physics.SphereCastNonAlloc(from, 0.2f, direction / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (var i = 0; i < count; i++)
        {
            var hitTransform = hits[i].collider.transform;
            if (hitTransform == robot || hitTransform.IsChildOf(robot))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void OnGUI()
    {
        var scale = Mathf.Max(1f, Screen.height / 720f);
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float x = 12f;
        const float y = 82f;
        GUI.Box(new Rect(x, y, 250f, 62f), GUIContent.none);
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(x + 8f, y + 2f, 234f, 24f), "Câmera: " + (robotView ? "Robô" : "Geral"), style);
        if (GUI.Button(new Rect(x + 8f, y + 30f, 112f, 24f), "Geral")) { SetRobotView(false); }
        if (GUI.Button(new Rect(x + 130f, y + 30f, 112f, 24f), "Robô")) { SetRobotView(true); }

        GUI.matrix = matrix;
    }
}
