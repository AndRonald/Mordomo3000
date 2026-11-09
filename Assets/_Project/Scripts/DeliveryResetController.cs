using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Pressing R clears every delivered package, as if it had been collected.</summary>
public sealed class DeliveryResetController : MonoBehaviour
{
    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.rKey.wasPressedThisFrame)
        {
            return;
        }

        var removed = 0;
        foreach (var destination in FindObjectsByType<DeliveryDestination>(FindObjectsSortMode.None))
        {
            removed += destination.ClearAll();
        }

        Debug.Log("Entregas reiniciadas: " + removed + " encomenda(s) removida(s).", this);
    }
}
