using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
    /// <summary>
    /// GameLauncher handles the game initialization and connection process.
    /// This replaces the old TestLauncher with a proper UI-driven approach.
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        private const string LogTag = "[GameLauncher]";
        
        [Header("UI References")]
        [SerializeField] private LauncherUI launcherUI;
        
        [Header("Auto-Launch Settings")]
        [SerializeField] private bool autoLaunchOnStart = false;
        [SerializeField] private string autoEmail = "test@example.com";
        [SerializeField] private string autoUsername = "Tester";
        [SerializeField] private string autoPassword = "password";
        [SerializeField] private bool autoRegisterFirst = false;
        [SerializeField] private string autoMode = "practice";
        
        private void Start()
        {
            Debug.Log($"{LogTag} GameLauncher started");
            
            if (autoLaunchOnStart)
            {
                Debug.Log($"{LogTag} Auto-launching with predefined settings");
                AutoLaunch().Forget();
            }
            else
            {
                Debug.Log($"{LogTag} Showing launcher UI for manual input");
                ShowLauncherUI();
            }
        }
        
        private void ShowLauncherUI()
        {
            if (launcherUI != null)
            {
                launcherUI.ShowLauncher();
            }
            else
            {
                Debug.LogError($"{LogTag} LauncherUI reference is null. Please assign it in the inspector.");
            }
        }
        
        private async UniTask AutoLaunch()
        {
            try
            {
                Debug.Log($"{LogTag} Starting auto-launch process");
                
                if (AuthManager.Instance == null)
                {
                    Debug.LogError($"{LogTag} AuthManager.Instance is null. Aborting.");
                    ShowLauncherUI(); // Show launcher UI as fallback before returning
                    return;
                }

                if (NetworkManager.Instance == null)
                {
                    Debug.LogError($"{LogTag} NetworkManager.Instance is null. Aborting.");
                    ShowLauncherUI(); // Show launcher UI as fallback before returning
                    return;
                }

                // Register if requested
                if (autoRegisterFirst)
                {
                    Debug.Log($"{LogTag} Auto-registering username='{autoUsername}' and email='<redacted>'...");
                    bool registered = await AuthManager.Instance.Register(autoEmail, autoPassword, autoUsername);
                    Debug.Log($"{LogTag} Auto-register returned: {registered}");
                    if (!registered)
                    {
                        Debug.LogError($"{LogTag} Auto-registration failed");
                        ShowLauncherUI(); // Fall back to UI
                        return;
                    }
                    Debug.Log($"{LogTag} Auto-registration succeeded");
                }

                // Login
                Debug.Log($"{LogTag} Auto-logging in with username='{autoUsername}'...");
                bool loggedIn = await AuthManager.Instance.LoginWithUsername(autoUsername, autoPassword);
                Debug.Log($"{LogTag} Auto-login returned: {loggedIn}");
                if (!loggedIn)
                {
                    Debug.LogError($"{LogTag} Auto-login failed");
                    ShowLauncherUI(); // Fall back to UI
                    return;
                }
                Debug.Log($"{LogTag} Auto-login succeeded");

                // Fetch config
                Debug.Log($"{LogTag} Auto-fetching config...");
                bool configOk = await NetworkManager.Instance.FetchConfig();
                Debug.Log($"{LogTag} Auto-FetchConfig returned: {configOk}");
                if (!configOk)
                {
                    Debug.LogError($"{LogTag} Auto-FetchConfig failed");
                    ShowLauncherUI(); // Fall back to UI
                    return;
                }

                // Join queue
                Debug.Log($"{LogTag} Auto-joining matchmaking queue with mode='{autoMode}'...");
                await NetworkManager.Instance.JoinQueue(autoMode);
                Debug.Log($"{LogTag} Auto-JoinQueue completed successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LogTag} Exception during auto-launch: {ex}");
                ShowLauncherUI(); // Fall back to UI on error
            }
        }
        
        /// <summary>
        /// Public method to manually trigger the launcher UI
        /// </summary>
        public void ShowLauncher()
        {
            ShowLauncherUI();
        }
        
        /// <summary>
        /// Public method to hide the launcher UI
        /// </summary>
        public void HideLauncher()
        {
            if (launcherUI != null)
            {
                launcherUI.HideLauncher();
            }
        }
    }
}