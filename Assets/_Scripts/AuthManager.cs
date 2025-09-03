
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using System;

namespace ManaGambit
{
    public class AuthManager : MonoBehaviour
    {
        private const string LogTag = "[AuthManager]";
        private const string RegisterPath = "auth/register";
        private const string LoginPath = "auth/login";
        public static AuthManager Instance { get; private set; }

        // Server URL is now centralized in ServerConfig

        public string Token { get; private set; }
        public string UserId { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public async UniTask<bool> Register(string email, string password, string username)
        {
            string url = ServerConfig.ServerUrl + RegisterPath;
            var payload = new RegisterRequest { email = email, password = password, username = username };
            string json = JsonUtility.ToJson(payload);
            Debug.Log($"{LogTag} POST {url} body={json}");
            var request = new UnityWebRequest(url, "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            await UniTask.WaitUntil(() => operation.isDone);

            Debug.Log($"{LogTag} Register responseCode={(long)request.responseCode} result={request.result} error={request.error} body={request.downloadHandler.text}");
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LogTag} Register failed: {request.error} (HTTP {(long)request.responseCode})");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);
                Token = response.token;
                UserId = response.user.id;
                Debug.Log($"{LogTag} Register success. userId={UserId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} Failed to parse register response: {ex}\nBody: {request.downloadHandler.text}");
                return false;
            }
            return true;
        }

        public async UniTask<bool> Login(string email, string password)
        {
            string url = ServerConfig.ServerUrl + LoginPath;
            var payload = new LoginRequest { email = email, password = password };
            string json = JsonUtility.ToJson(payload);
            Debug.Log($"{LogTag} POST {url} body={json}");
            var request = new UnityWebRequest(url, "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            await UniTask.WaitUntil(() => operation.isDone);

            Debug.Log($"{LogTag} Login responseCode={(long)request.responseCode} result={request.result} error={request.error} body={request.downloadHandler.text}");
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LogTag} Login failed: {request.error} (HTTP {(long)request.responseCode})");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);
                Token = response.token;
                UserId = response.user.id;
                Debug.Log($"{LogTag} Login success. userId={UserId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} Failed to parse login response: {ex}\nBody: {request.downloadHandler.text}");
                return false;
            }
            return true;
        }

        public async UniTask<bool> LoginWithUsername(string username, string password)
        {
            string url = ServerConfig.ServerUrl + LoginPath;
            var payload = new UsernameLoginRequest { username = username, password = password };
            string json = JsonUtility.ToJson(payload);
            Debug.Log($"{LogTag} POST {url} body={json}");
            var request = new UnityWebRequest(url, "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            await UniTask.WaitUntil(() => operation.isDone);

            Debug.Log($"{LogTag} Login responseCode={(long)request.responseCode} result={request.result} error={request.error} body={request.downloadHandler.text}");
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LogTag} Login failed: {request.error} (HTTP {(long)request.responseCode})");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);
                Token = response.token;
                UserId = response.user.id;
                Debug.Log($"{LogTag} Login success. userId={UserId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} Failed to parse login response: {ex}\\nBody: {request.downloadHandler.text}");
                return false;
            }
            return true;
        }

        [Serializable]
        private class AuthResponse
        {
            public string token;
            public User user;
        }

        [Serializable]
        private class User
        {
            public string id;
            public string email;
            public string username;
        }

        [Serializable]
        private class RegisterRequest
        {
            public string email;
            public string password;
            public string username;
        }

        [Serializable]
        private class LoginRequest
        {
            public string email;
            public string password;
        }

        [Serializable]
        private class UsernameLoginRequest
        {
            public string username;
            public string password;
        }
    }
}
