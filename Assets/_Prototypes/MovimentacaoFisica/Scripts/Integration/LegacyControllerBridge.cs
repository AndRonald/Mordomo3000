using UnityEngine;

namespace MordomoProto.Fisica
{
    // Ponte somente-leitura: expõe ao adaptador APENAS se o controlador legado está navegando.
    // Não envia destinos, não chama MoveTo/Stop e não decide nada de navegação.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public sealed class LegacyControllerBridge : MonoBehaviour
    {
        public RobotNavigationController controller;
        public PhysicalDriveAdapter adapter;

        void Awake()
        {
            if (controller == null) controller = GetComponent<RobotNavigationController>();
            if (adapter == null) adapter = GetComponent<PhysicalDriveAdapter>();
            if (adapter != null) adapter.ExternalNavigating = IsNavigating;
        }

        bool IsNavigating()
        {
            return controller != null && controller.CurrentNavigationState == RobotNavigationState.Navigating;
        }

        void OnDestroy() { if (adapter != null) adapter.ExternalNavigating = null; }
    }
}
