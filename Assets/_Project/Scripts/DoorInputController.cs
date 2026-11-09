using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Pressing E opens every closed door, or closes all of them when all are already open.</summary>
public sealed class DoorInputController : MonoBehaviour
{
    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
        {
            return;
        }

        var doors = FindObjectsByType<DoorController>(FindObjectsSortMode.None);
        var anyClosed = false;
        foreach (var door in doors)
        {
            if (!door.IsOpen)
            {
                anyClosed = true;
            }
        }

        foreach (var door in doors)
        {
            door.SetOpen(anyClosed);
        }
    }
}
