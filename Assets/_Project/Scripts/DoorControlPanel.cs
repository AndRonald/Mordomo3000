using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Compact, collapsible HUD panel that lists the eight doors and lets each one be opened or closed individually.
/// It owns no door state: it reads DoorController.IsOpen every frame and only calls DoorController.SetOpen on the
/// single door whose button was pressed. The state shown is the commanded state (the sliding animation takes about 0.9 s).
/// Closing a door is refused while the robot overlaps that door's opening or the path its panel sweeps on the rail
/// (checked with the robot's real box colliders plus a safety margin). If this cannot be determined, closing is blocked.
/// </summary>
public sealed class DoorControlPanel : MonoBehaviour
{
    private sealed class Entry
    {
        public string objectName;
        public string label;
        public DoorController door;
    }

    private readonly List<Entry> entries = new List<Entry>
    {
        new Entry { objectName = "Porta_Cozinha_Escritorio", label = "Cozinha – Escritório" },
        new Entry { objectName = "Porta_Escritorio_Hall", label = "Escritório – Hall" },
        new Entry { objectName = "Porta_Cozinha_Sala", label = "Cozinha – Sala" },
        new Entry { objectName = "Porta_Sala_Hall", label = "Sala – Hall" },
        new Entry { objectName = "Porta_Banheiro", label = "Banheiro" },
        new Entry { objectName = "Porta_Quarto1_Cozinha", label = "Quarto 1 – Cozinha" },
        new Entry { objectName = "Porta_Quarto2_Corredor", label = "Quarto 2 – Corredor" },
        new Entry { objectName = "Porta_Corredor_Sala", label = "Corredor – Sala" },
    };

    [SerializeField] private bool startExpanded;
    [SerializeField, Min(0f)] private float safetyMargin = 0.10f;
    [SerializeField] private Transform robot;

    private readonly List<Collider> robotColliders = new List<Collider>();
    private bool expanded;
    private float nextScanTime;

    private void Start()
    {
        expanded = startExpanded;
        ResolveDoors();
    }

    private void ResolveDoors()
    {
        nextScanTime = Time.unscaledTime + 1f;
        var found = FindObjectsByType<DoorController>();
        foreach (var entry in entries)
        {
            entry.door = null;
            foreach (var door in found)
            {
                if (door != null && door.gameObject.name == entry.objectName)
                {
                    entry.door = door;
                    break;
                }
            }
        }
    }

    private bool AllDoorsResolved()
    {
        foreach (var entry in entries)
        {
            if (entry.door == null)
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureRobot()
    {
        if (robot != null)
        {
            return;
        }

        var navigation = FindAnyObjectByType<RobotNavigationController>();
        if (navigation != null)
        {
            robot = navigation.transform;
        }
    }

    /// <summary>
    /// Returns true when closing the door must be refused, with the reason. Any case that cannot be verified counts as blocked.
    /// </summary>
    public bool TryGetCloseBlockReason(DoorController door, out string reason)
    {
        reason = null;
        if (door == null)
        {
            reason = "Porta não encontrada";
            return true;
        }

        EnsureRobot();
        if (robot == null)
        {
            reason = "Robô não encontrado: não dá para verificar o vão";
            return true;
        }

        var obstacle = door.GetComponent<NavMeshObstacle>();
        var rail = door.transform.Find("Trilho");
        if (obstacle == null || rail == null)
        {
            reason = "Vão não verificável";
            return true;
        }

        // Zone 1: the opening itself (obstacle box). Zone 2: the strip swept by the panel along the rail.
        var doorScale = door.transform.lossyScale;
        var openingCenter = door.transform.TransformPoint(obstacle.center);
        var openingHalf = new Vector2(
            Mathf.Abs(obstacle.size.x * doorScale.x) * 0.5f + safetyMargin,
            Mathf.Abs(obstacle.size.z * doorScale.z) * 0.5f + safetyMargin);
        var openingYaw = door.transform.eulerAngles.y;

        var railScale = rail.lossyScale;
        var railHalf = new Vector2(
            Mathf.Abs(railScale.x) * 0.5f + safetyMargin,
            Mathf.Abs(railScale.z) * 0.5f + safetyMargin);
        var railYaw = rail.eulerAngles.y;

        robot.GetComponentsInChildren(false, robotColliders);
        var usable = 0;
        foreach (var robotCollider in robotColliders)
        {
            if (!robotCollider.enabled || robotCollider.isTrigger)
            {
                continue;
            }

            var box = robotCollider as BoxCollider;
            if (box == null)
            {
                reason = "Colisor do robô não verificável";
                return true;
            }

            usable++;
            var scale = box.transform.lossyScale;
            var center = box.transform.TransformPoint(box.center);
            var half = new Vector2(Mathf.Abs(box.size.x * scale.x) * 0.5f, Mathf.Abs(box.size.z * scale.z) * 0.5f);
            var yaw = box.transform.eulerAngles.y;
            var robotCenter = new Vector2(center.x, center.z);

            if (RectanglesOverlap(robotCenter, half, yaw, new Vector2(openingCenter.x, openingCenter.z), openingHalf, openingYaw) ||
                RectanglesOverlap(robotCenter, half, yaw, new Vector2(rail.position.x, rail.position.z), railHalf, railYaw))
            {
                reason = "Robô no vão";
                return true;
            }
        }

        if (usable == 0)
        {
            reason = "Robô sem colisor: não dá para verificar o vão";
            return true;
        }

        return false;
    }

    // Separating-axis test between two rectangles in the XZ plane (centre, half extents, yaw in degrees).
    private static bool RectanglesOverlap(Vector2 centerA, Vector2 halfA, float yawA, Vector2 centerB, Vector2 halfB, float yawB)
    {
        var axisA0 = AxisFromYaw(yawA);
        var axisA1 = new Vector2(-axisA0.y, axisA0.x);
        var axisB0 = AxisFromYaw(yawB);
        var axisB1 = new Vector2(-axisB0.y, axisB0.x);
        var delta = centerB - centerA;

        return OverlapsOnAxis(axisA0, delta, halfA, axisA0, axisA1, halfB, axisB0, axisB1) &&
               OverlapsOnAxis(axisA1, delta, halfA, axisA0, axisA1, halfB, axisB0, axisB1) &&
               OverlapsOnAxis(axisB0, delta, halfA, axisA0, axisA1, halfB, axisB0, axisB1) &&
               OverlapsOnAxis(axisB1, delta, halfA, axisA0, axisA1, halfB, axisB0, axisB1);
    }

    private static bool OverlapsOnAxis(Vector2 axis, Vector2 delta, Vector2 halfA, Vector2 a0, Vector2 a1, Vector2 halfB, Vector2 b0, Vector2 b1)
    {
        var radiusA = halfA.x * Mathf.Abs(Vector2.Dot(a0, axis)) + halfA.y * Mathf.Abs(Vector2.Dot(a1, axis));
        var radiusB = halfB.x * Mathf.Abs(Vector2.Dot(b0, axis)) + halfB.y * Mathf.Abs(Vector2.Dot(b1, axis));
        return Mathf.Abs(Vector2.Dot(delta, axis)) <= radiusA + radiusB;
    }

    private static Vector2 AxisFromYaw(float yawDegrees)
    {
        var radians = yawDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)); // local +X of a Y-rotated transform, in XZ
    }

    private void OnGUI()
    {
        if (!AllDoorsResolved() && Time.unscaledTime >= nextScanTime)
        {
            ResolveDoors();
        }

        var scale = Mathf.Max(1f, Screen.height / 720f);
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float x = 12f;
        const float y = 152f;
        const float width = 250f;
        const float headerHeight = 30f;
        const float rowHeight = 28f;
        const float warningHeight = 15f;

        var openCount = 0;
        foreach (var entry in entries)
        {
            if (entry.door != null && entry.door.IsOpen)
            {
                openCount++;
            }
        }

        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = Color.white;

        if (!expanded)
        {
            GUI.Box(new Rect(x, y, width, headerHeight), GUIContent.none);
            GUI.Label(new Rect(x + 8f, y + 3f, 190f, 24f), "Portas: " + openCount + "/" + entries.Count + " abertas", titleStyle);
            if (GUI.Button(new Rect(x + width - 40f, y + 3f, 32f, 24f), "+"))
            {
                expanded = true;
            }

            GUI.matrix = matrix;
            return;
        }

        var rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        rowStyle.normal.textColor = Color.white;
        var stateStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        var warnStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        warnStyle.normal.textColor = new Color(1f, 0.75f, 0.3f);
        var noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        noteStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

        // Pre-compute close blocks so the background box fits the content.
        var reasons = new string[entries.Count];
        var extra = 0f;
        for (var i = 0; i < entries.Count; i++)
        {
            var door = entries[i].door;
            if (door != null && door.IsOpen && TryGetCloseBlockReason(door, out var reason))
            {
                reasons[i] = reason;
                extra += warningHeight;
            }
        }

        var totalHeight = headerHeight + rowHeight * entries.Count + extra + 22f;
        GUI.Box(new Rect(x, y, width, totalHeight), GUIContent.none);
        GUI.Label(new Rect(x + 8f, y + 3f, 190f, 24f), "Portas: " + openCount + "/" + entries.Count + " abertas", titleStyle);
        if (GUI.Button(new Rect(x + width - 40f, y + 3f, 32f, 24f), "-"))
        {
            expanded = false;
        }

        var rowY = y + headerHeight;
        var previousEnabled = GUI.enabled;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var door = entry.door;
            GUI.Label(new Rect(x + 8f, rowY + 2f, 122f, 22f), entry.label, rowStyle);

            if (door == null)
            {
                stateStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(new Rect(x + 130f, rowY + 2f, 60f, 22f), "—", stateStyle);
                GUI.enabled = false;
                GUI.Button(new Rect(x + 192f, rowY + 2f, 52f, 22f), "?");
                GUI.enabled = previousEnabled;
                rowY += rowHeight;
                continue;
            }

            var isOpen = door.IsOpen;
            stateStyle.normal.textColor = isOpen ? new Color(0.45f, 0.9f, 0.45f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(x + 130f, rowY + 2f, 60f, 22f), isOpen ? "Aberta" : "Fechada", stateStyle);

            var blocked = reasons[i] != null;
            GUI.enabled = previousEnabled && !blocked;
            if (GUI.Button(new Rect(x + 192f, rowY + 2f, 52f, 22f), isOpen ? "Fechar" : "Abrir"))
            {
                if (!isOpen)
                {
                    door.SetOpen(true);
                }
                else if (!TryGetCloseBlockReason(door, out _))
                {
                    door.SetOpen(false);
                }
            }

            GUI.enabled = previousEnabled;
            rowY += rowHeight;
            if (blocked)
            {
                GUI.Label(new Rect(x + 8f, rowY - 8f, width - 16f, warningHeight + 4f), "⚠ " + reasons[i] + " – fechamento bloqueado", warnStyle);
                rowY += warningHeight;
            }
        }

        GUI.Label(new Rect(x + 8f, rowY + 1f, width - 16f, 18f), "Estado comandado; animação ≈ 0,9 s", noteStyle);
        GUI.matrix = matrix;
    }
}
