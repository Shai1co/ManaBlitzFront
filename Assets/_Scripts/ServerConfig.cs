using UnityEngine;

namespace ManaGambit
{
    /// <summary>
    /// Centralized server configuration to avoid duplication between AuthManager and NetworkManager
    /// </summary>
    public static class ServerConfig
    {
        // Default server URL - can be overridden in development builds
        private const string DefaultServerUrl = "https://manablitz.pareto.solutions/";
        private const string FallbackServerUrl = "https://manablitz.onrender.com/";
        
        /// <summary>
        /// Gets the current server URL. In development builds, this can be overridden via PlayerPrefs.
        /// </summary>
        public static string ServerUrl
        {
            get
            {
                // Allow override via PlayerPrefs in development builds
                if (Debug.isDebugBuild)
                {
                    string overrideUrl = PlayerPrefs.GetString("ServerUrlOverride", "");
                    if (!string.IsNullOrEmpty(overrideUrl))
                    {
                        return overrideUrl;
                    }
                }
                return DefaultServerUrl;
            }
        }
        
        /// <summary>
        /// Sets a custom server URL override (only works in development builds)
        /// </summary>
        /// <param name="url">The server URL to use</param>
        public static void SetServerUrlOverride(string url)
        {
            if (Debug.isDebugBuild)
            {
                PlayerPrefs.SetString("ServerUrlOverride", url);
                PlayerPrefs.Save();
            }
        }
        
        /// <summary>
        /// Clears the server URL override
        /// </summary>
        public static void ClearServerUrlOverride()
        {
            if (Debug.isDebugBuild)
            {
                PlayerPrefs.DeleteKey("ServerUrlOverride");
                PlayerPrefs.Save();
            }
        }
        
        /// <summary>
        /// Gets the fallback server URL
        /// </summary>
        public static string FallbackUrl => FallbackServerUrl;
    }
}
