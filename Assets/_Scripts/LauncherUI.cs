using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading;
using System;

namespace ManaGambit
{
    public class LauncherUI : MonoBehaviour
    {
        private const string LogTag = "[LauncherUI]";
        private const string MODE_DROPDOWN_PREF_KEY = "LauncherUI_ModeDropdown";
        private const string EMAIL_PREF_KEY = "LauncherUI_Email";
        private const string USERNAME_PREF_KEY = "LauncherUI_Username";
        private const string PASSWORD_PREF_KEY = "LauncherUI_Password";
        
        [Header("UI References")]
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Dropdown modeDropdown;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button registerButton;
        
        [Header("Settings")]
        [SerializeField] private string defaultEmail = "test@example.com";
        [SerializeField] private string defaultUsername = "Tester";
        [SerializeField] private string defaultPassword = "password";
        
        private bool isProcessing = false;
        
        // Event management fields
        private CancellationTokenSource cancellationTokenSource;
        private UnityEngine.Events.UnityAction loginButtonHandler;
        private UnityEngine.Events.UnityAction registerButtonHandler;
        
        private void Start()
        {
            InitializeUI();
            SetupEventListeners();
        }
        
        private void OnDisable()
        {
            CleanupEventListeners();
        }
        
        private void InitializeUI()
        {
            // Load saved values from PlayerPrefs or use defaults
            string savedEmail = PlayerPrefs.GetString(EMAIL_PREF_KEY, defaultEmail);
            string savedUsername = PlayerPrefs.GetString(USERNAME_PREF_KEY, defaultUsername);
            string savedPassword = PlayerPrefs.GetString(PASSWORD_PREF_KEY, defaultPassword);
            
            // Set values
            if (emailInput != null)
                emailInput.text = savedEmail;
            
            if (usernameInput != null)
                usernameInput.text = savedUsername;
            
            if (passwordInput != null)
            {
                passwordInput.contentType = TMP_InputField.ContentType.Password;
                passwordInput.text = savedPassword;
                passwordInput.ForceLabelUpdate();
            }
            
            // Setup mode dropdown
            if (modeDropdown != null)
            {
                modeDropdown.options.Clear();
                modeDropdown.AddOptions(new System.Collections.Generic.List<string> { "Practice", "Arena" });
                
                // Load saved dropdown value from PlayerPrefs
                int savedModeIndex = PlayerPrefs.GetInt(MODE_DROPDOWN_PREF_KEY, 0);
                modeDropdown.SetValueWithoutNotify(savedModeIndex);
            }
        }
        
        private void SetupEventListeners()
        {
            // Clean up any existing listeners and tokens
            CleanupEventListeners();
            
            // Create new cancellation token source
            cancellationTokenSource = new CancellationTokenSource();
            var ct = cancellationTokenSource.Token;
            
            if (loginButton != null)
            {
                loginButtonHandler = () => OnLoginClicked(ct).Forget();
                loginButton.onClick.AddListener(loginButtonHandler);
            }
            
            if (registerButton != null)
            {
                registerButtonHandler = () => OnRegisterClicked(ct).Forget();
                registerButton.onClick.AddListener(registerButtonHandler);
            }
            
            // Setup mode dropdown event listener
            if (modeDropdown != null)
            {
                modeDropdown.onValueChanged.AddListener(OnModeDropdownChanged);
            }
            
            // Setup input field event listeners
            if (emailInput != null)
            {
                emailInput.onValueChanged.AddListener(OnEmailChanged);
            }
            
            if (usernameInput != null)
            {
                usernameInput.onValueChanged.AddListener(OnUsernameChanged);
            }
            
            if (passwordInput != null)
            {
                passwordInput.onValueChanged.AddListener(OnPasswordChanged);
            }
        }
        
        private void CleanupEventListeners()
        {
            // Remove event listeners
            if (loginButton != null && loginButtonHandler != null)
            {
                loginButton.onClick.RemoveListener(loginButtonHandler);
                loginButtonHandler = null;
            }
            
            if (registerButton != null && registerButtonHandler != null)
            {
                registerButton.onClick.RemoveListener(registerButtonHandler);
                registerButtonHandler = null;
            }
            
            // Remove mode dropdown event listener
            if (modeDropdown != null)
            {
                modeDropdown.onValueChanged.RemoveListener(OnModeDropdownChanged);
            }
            
            // Remove input field event listeners
            if (emailInput != null)
            {
                emailInput.onValueChanged.RemoveListener(OnEmailChanged);
            }
            
            if (usernameInput != null)
            {
                usernameInput.onValueChanged.RemoveListener(OnUsernameChanged);
            }
            
            if (passwordInput != null)
            {
                passwordInput.onValueChanged.RemoveListener(OnPasswordChanged);
            }
            
            // Cancel and dispose cancellation token source
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }
        
        private async UniTaskVoid OnLoginClicked(CancellationToken cancellationToken)
        {
            if (isProcessing) return;
            
            Debug.Log($"{LogTag} Login button clicked");
            
            string email = emailInput?.text ?? "";
            string username = usernameInput?.text ?? "";
            string password = passwordInput?.text ?? "";
            string mode = GetSelectedMode();
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Debug.LogError($"{LogTag} Username and password are required");
                return;
            }
            
            await ProcessLogin(email, username, password, mode, false, cancellationToken);
        }
        
        private async UniTaskVoid OnRegisterClicked(CancellationToken cancellationToken)
        {
            if (isProcessing) return;
            
            Debug.Log($"{LogTag} Register button clicked");
            
            string email = emailInput?.text ?? "";
            string username = usernameInput?.text ?? "";
            string password = passwordInput?.text ?? "";
            string mode = GetSelectedMode();
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Debug.LogError($"{LogTag} Email, username, and password are required for registration");
                return;
            }
            
            await ProcessLogin(email, username, password, mode, true, cancellationToken);
        }
        
        private string GetSelectedMode()
        {
            if (modeDropdown == null) return "practice";
            
            return modeDropdown.value == 0 ? "practice" : "arena";
        }
        
        private void OnModeDropdownChanged(int value)
        {
            if (modeDropdown != null)
            {
                PlayerPrefs.SetInt(MODE_DROPDOWN_PREF_KEY, value);
                PlayerPrefs.Save();
                Debug.Log($"{LogTag} Mode dropdown changed to index {value}, saved to PlayerPrefs");
            }
        }
        
        private void OnEmailChanged(string value)
        {
            PlayerPrefs.SetString(EMAIL_PREF_KEY, value);
            PlayerPrefs.Save();
            Debug.Log($"{LogTag} Email changed, saved to PlayerPrefs");
        }
        
        private void OnUsernameChanged(string value)
        {
            PlayerPrefs.SetString(USERNAME_PREF_KEY, value);
            PlayerPrefs.Save();
            Debug.Log($"{LogTag} Username changed, saved to PlayerPrefs");
        }
        
        private void OnPasswordChanged(string value)
        {
            PlayerPrefs.SetString(PASSWORD_PREF_KEY, value);
            PlayerPrefs.Save();
            Debug.Log($"{LogTag} Password changed, saved to PlayerPrefs");
        }
        
        private async UniTask ProcessLogin(string email, string username, string password, string mode, bool registerFirst, CancellationToken cancellationToken)
        {
            isProcessing = true;
            SetButtonsInteractable(false);
            
            // Create a timeout for each operation (30 seconds per operation)
            const int OPERATION_TIMEOUT_SECONDS = 30;
            
            // Track success to avoid showing launcher on successful completion
            bool succeeded = false;
            
            try
            {
                Debug.Log($"{LogTag} Starting authentication process. Register first: {registerFirst}");
                
                if (AuthManager.Instance == null)
                {
                    Debug.LogError($"{LogTag} AuthManager.Instance is null. Aborting.");
                    return;
                }

                if (NetworkManager.Instance == null)
                {
                    Debug.LogError($"{LogTag} NetworkManager.Instance is null. Aborting.");
                    return;
                }

                // Register if requested
                if (registerFirst)
                {
                    try
                    {
                        Debug.Log($"{LogTag} Attempting registration for username='{username}' and email='{email}'...");
                        bool registered = await AuthManager.Instance.Register(email, password, username)
                            .Timeout(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS))
                            .AttachExternalCancellation(cancellationToken);
                        
                        Debug.Log($"{LogTag} Register returned: {registered}");
                        if (!registered)
                        {
                            Debug.LogError($"{LogTag} Registration failed - operation returned false");
                            return;
                        }
                        Debug.Log($"{LogTag} Registration succeeded");
                    }
                    catch (TimeoutException)
                    {
                        Debug.LogError($"{LogTag} Registration timed out after {OPERATION_TIMEOUT_SECONDS} seconds");
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.LogError($"{LogTag} Registration was cancelled");
                        return;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"{LogTag} Registration failed with exception: {ex.Message}");
                        return;
                    }
                }

                // Login
                try
                {
                    Debug.Log($"{LogTag} Attempting login with username='{username}'...");
                    bool loggedIn = await AuthManager.Instance.LoginWithUsername(username, password)
                        .Timeout(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS))
                        .AttachExternalCancellation(cancellationToken);
                    
                    Debug.Log($"{LogTag} Login returned: {loggedIn}");
                    if (!loggedIn)
                    {
                        Debug.LogError($"{LogTag} Login failed - operation returned false");
                        return;
                    }
                    Debug.Log($"{LogTag} Login succeeded");
                }
                catch (TimeoutException)
                {
                    Debug.LogError($"{LogTag} Login timed out after {OPERATION_TIMEOUT_SECONDS} seconds");
                    return;
                }
                catch (OperationCanceledException)
                {
                    Debug.LogError($"{LogTag} Login was cancelled");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"{LogTag} Login failed with exception: {ex.Message}");
                    return;
                }

                // Fetch config
                try
                {
                    Debug.Log($"{LogTag} Fetching config before matchmaking...");
                    bool configOk = await NetworkManager.Instance.FetchConfig()
                        .Timeout(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS))
                        .AttachExternalCancellation(cancellationToken);
                    
                    Debug.Log($"{LogTag} FetchConfig returned: {configOk}");
                    if (!configOk)
                    {
                        Debug.LogError($"{LogTag} FetchConfig failed - operation returned false");
                        return;
                    }
                    Debug.Log($"{LogTag} FetchConfig succeeded");
                }
                catch (TimeoutException)
                {
                    Debug.LogError($"{LogTag} FetchConfig timed out after {OPERATION_TIMEOUT_SECONDS} seconds");
                    return;
                }
                catch (OperationCanceledException)
                {
                    Debug.LogError($"{LogTag} FetchConfig was cancelled");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"{LogTag} FetchConfig failed with exception: {ex.Message}");
                    return;
                }

                // Join queue
                try
                {
                    Debug.Log($"{LogTag} Attempting to join matchmaking queue with mode='{mode}'...");
                    await NetworkManager.Instance.JoinQueue(mode)
                        .Timeout(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS))
                        .AttachExternalCancellation(cancellationToken);
                    
                    Debug.Log($"{LogTag} JoinQueue completed successfully");
                    
                    // Mark as succeeded before hiding the launcher UI
                    succeeded = true;
                    
                    // Hide the launcher UI after successful connection
                    gameObject.SetActive(false);
                }
                catch (TimeoutException)
                {
                    Debug.LogError($"{LogTag} JoinQueue timed out after {OPERATION_TIMEOUT_SECONDS} seconds");
                    return;
                }
                catch (OperationCanceledException)
                {
                    Debug.LogError($"{LogTag} JoinQueue was cancelled");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"{LogTag} JoinQueue failed with exception: {ex.Message}");
                    return;
                }
            }
            finally
            {
                isProcessing = false;
                SetButtonsInteractable(true);
                
                // Only show launcher if the operation failed or an exception occurred
                if (!succeeded)
                {
                    ShowLauncher(); // Ensure UI is visible when buttons are re-enabled
                }
            }
        }
        
        private void SetButtonsInteractable(bool interactable)
        {
            if (loginButton != null)
                loginButton.interactable = interactable;
            
            if (registerButton != null)
                registerButton.interactable = interactable;
        }
        
        public void ShowLauncher()
        {
            gameObject.SetActive(true);
        }
        
        public void HideLauncher()
        {
            gameObject.SetActive(false);
        }
    }
}