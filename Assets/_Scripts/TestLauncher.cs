
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
	public class TestLauncher : MonoBehaviour
	{
		private const string LogTag = "[TestLauncher]";
		[SerializeField] private string email = "test@example.com";
		[SerializeField] private string password = "password";
		[SerializeField] private string username = "Tester";
		[SerializeField] private bool registerInstead = false;
		[SerializeField] private string mode = "practice"; // "practice" or "arena"

		private async void Start()
		{
			Debug.Log($"{LogTag} Start() called. registerInstead={registerInstead}, email={email}, username={username}");

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

			try
			{
				if (registerInstead)
				{
					Debug.Log($"{LogTag} Attempting registration for username='{username}' and email='{email}'...");
					bool registered = await AuthManager.Instance.Register(email, password, username);
					Debug.Log($"{LogTag} Register returned: {registered}");
					if (!registered)
					{
						Debug.LogError($"{LogTag} Register failed");
						return;
					}
					Debug.Log($"{LogTag} Registration succeeded");
				}

				Debug.Log($"{LogTag} Attempting login with username='{username}'...");
				bool loggedIn = await AuthManager.Instance.LoginWithUsername(username, password);
				Debug.Log($"{LogTag} Login returned: {loggedIn}");
				if (!loggedIn)
				{
					Debug.LogError($"{LogTag} Login failed");
					return;
				}
				Debug.Log($"{LogTag} Login succeeded");

				Debug.Log($"{LogTag} Fetching config before matchmaking...");
				bool configOk = await NetworkManager.Instance.FetchConfig();
				Debug.Log($"{LogTag} FetchConfig returned: {configOk}");
				if (!configOk)
				{
					Debug.LogError($"{LogTag} FetchConfig failed");
					return;
				}

				Debug.Log($"{LogTag} Attempting to join matchmaking queue with mode='{mode}'...");
				await NetworkManager.Instance.JoinQueue(mode);
				Debug.Log($"{LogTag} JoinQueue completed");
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"{LogTag} Exception during Start(): {ex}");
			}
		}
	}
}
