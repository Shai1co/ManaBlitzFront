using UnityEngine;
using System;

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
        private const string OverrideKey = "ServerUrlOverride";

        // Cached override URL to avoid repeated PlayerPrefs reads
        private static string _cachedOverrideUrl;
        private static bool _hasCachedOverride;

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
                    string overrideUrl = GetValidatedOverrideUrl();
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
                // Validate the provided URL before persisting
                if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
                {
                    Debug.LogError($"[ServerConfig] Invalid server URL override provided: '{url}'. URL must be a valid absolute URI.");
                    return;
                }

                // Get normalized value from the URI
                string normalizedUrl = ValidateAndNormalizeUrl(uri.AbsoluteUri);
                
                // Store the normalized value
                PlayerPrefs.SetString(OverrideKey, normalizedUrl);
                PlayerPrefs.Save();
                
                // Prime the in-memory cache to avoid extra read on next access
                _hasCachedOverride = true;
                _cachedOverrideUrl = normalizedUrl;
                
                Debug.Log($"[ServerConfig] Server URL override set successfully: '{normalizedUrl}'");
            }
        }

        /// <summary>
        /// Clears the server URL override
        /// </summary>
        public static void ClearServerUrlOverride()
        {
            if (Debug.isDebugBuild)
            {
                PlayerPrefs.DeleteKey(OverrideKey);
                PlayerPrefs.Save();
                // Clear cache to force refresh on next read
                _hasCachedOverride = false;
                _cachedOverrideUrl = null;
            }
        }

        /// <summary>
        /// Gets the fallback server URL
        /// </summary>
        public static string FallbackUrl => FallbackServerUrl;

        /// <summary>
        /// Gets and validates the override URL from PlayerPrefs with caching
        /// </summary>
        /// <returns>Validated and normalized URL, or empty string if invalid</returns>
        private static string GetValidatedOverrideUrl()
        {
            // Return cached value if available
            if (_hasCachedOverride)
            {
                return _cachedOverrideUrl;
            }

            // Read from PlayerPrefs
            string overrideUrl = PlayerPrefs.GetString(OverrideKey, "");
            if (string.IsNullOrEmpty(overrideUrl))
            {
                // Cache empty result
                _cachedOverrideUrl = "";
                _hasCachedOverride = true;
                return "";
            }

            // Validate and normalize the URL
            string validatedUrl = ValidateAndNormalizeUrl(overrideUrl);

            // Cache the result
            _cachedOverrideUrl = validatedUrl;
            _hasCachedOverride = true;

            return validatedUrl;
        }

        /// <summary>
        /// Validates and normalizes a URL string
        /// </summary>
        /// <param name="url">The URL to validate and normalize</param>
        /// <returns>Normalized URL if valid, empty string if invalid</returns>
        private static string ValidateAndNormalizeUrl(string url)
        {
            // Try to create a valid URI
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return "";
            }

            // Only accept http and https schemes
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return "";
            }

            // Normalize the URL to ensure it has a trailing slash
            return NormalizeUrl(uri);
        }

        /// <summary>
        /// Normalizes a URI to ensure it has a trailing slash
        /// </summary>
        /// <param name="uri">The URI to normalize</param>
        /// <returns>Normalized URL string with trailing slash</returns>
        private static string NormalizeUrl(Uri uri)
        {
            var builder = new UriBuilder(uri);
            var path = string.IsNullOrEmpty(builder.Path) ? "/" : builder.Path;
            if (!path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                path += "/";
            }
            builder.Path = path;
            return builder.Uri.AbsoluteUri;
        }
    }
}
