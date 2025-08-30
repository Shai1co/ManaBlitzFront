using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
    public class ConnectionControlsUI : MonoBehaviour
    {
        [SerializeField] private Button reconnectButton; // optional; assign in Inspector
        [SerializeField] private Button disconnectButton; // optional; assign in Inspector
        [SerializeField] private bool showToasts = true;

        public void OnReconnectClick()
        {
            if (NetworkManager.Instance == null) return;
            if (showToasts && HudController.Instance != null) HudController.Instance.ShowToast("Reconnecting...", "info", 2);
            NetworkManager.Instance.Reconnect().Forget();
        }

        public void OnDisconnectClick()
        {
            if (NetworkManager.Instance == null) return;
            if (showToasts && HudController.Instance != null) HudController.Instance.ShowToast("Disconnecting...", "info", 2);
            NetworkManager.Instance.Disconnect().Forget();
        }
    }
}


