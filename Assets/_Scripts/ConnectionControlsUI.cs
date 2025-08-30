using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
    [DisallowMultipleComponent]
    public class ConnectionControlsUI : MonoBehaviour
    {
        private const string ToastInfo = "info";
        private const int ToastDurationSeconds = 2;
        [SerializeField] private Button reconnectButton; // optional; assign in Inspector
        [SerializeField] private Button disconnectButton; // optional; assign in Inspector
        [SerializeField] private bool showToasts = true;

        private bool isReconnecting = false;
        private bool isDisconnecting = false;

        private async void OnReconnectClick()
        {
            if (NetworkManager.Instance == null) return;
            if (isReconnecting) return; // Prevent re-entrant calls

            isReconnecting = true;
            if (reconnectButton != null) reconnectButton.interactable = false;

            try
            {
                if (showToasts) HudController.Instance?.ShowToast("Reconnecting...", ToastInfo, ToastDurationSeconds);
                await NetworkManager.Instance.Reconnect();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Reconnect failed: {ex.Message}");
                if (showToasts) HudController.Instance?.ShowToast($"Reconnect failed: {ex.Message}", "error", ToastDurationSeconds);
            }
            finally
            {
                isReconnecting = false;
                if (reconnectButton != null) reconnectButton.interactable = true;
            }
        }

        private async void OnDisconnectClick()
        {
            if (NetworkManager.Instance == null) return;
            if (isDisconnecting) return; // Prevent re-entrant calls

            isDisconnecting = true;
            if (disconnectButton != null) disconnectButton.interactable = false;

            try
            {
                if (showToasts) HudController.Instance?.ShowToast("Disconnecting...", ToastInfo, ToastDurationSeconds);
                await NetworkManager.Instance.Disconnect();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Disconnect failed: {ex.Message}");
                if (showToasts) HudController.Instance?.ShowToast($"Disconnect failed: {ex.Message}", "error", ToastDurationSeconds);
            }
            finally
            {
                isDisconnecting = false;
                if (disconnectButton != null) disconnectButton.interactable = true;
            }
        }

        private void OnEnable()
        {
            if (reconnectButton != null) reconnectButton.onClick.AddListener(OnReconnectClick);
            if (disconnectButton != null) disconnectButton.onClick.AddListener(OnDisconnectClick);
        }

        private void OnDisable()
        {
            if (reconnectButton != null) reconnectButton.onClick.RemoveListener(OnReconnectClick);
            if (disconnectButton != null) disconnectButton.onClick.RemoveListener(OnDisconnectClick);
        }
    }
}


