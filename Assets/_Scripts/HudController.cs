using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;

namespace ManaGambit
{
	public class HudController : MonoBehaviour
	{
		public static HudController Instance { get; private set; }

		[SerializeField] private TextMeshProUGUI statusText;
		[SerializeField] private TextMeshProUGUI countdownText;
		[SerializeField] private GameObject gameOverPanel;
		//[SerializeField] private TextMeshProUGUI gameOverText;
		[SerializeField, Tooltip("Container shown when the local player wins")] private GameObject gameOverWinContainer;
		[SerializeField, Tooltip("Text element for win state (optional if nested under container)")] private TextMeshProUGUI gameOverWinText;
		[SerializeField, Tooltip("Container shown when the local player loses")] private GameObject gameOverLoseContainer;
		[SerializeField, Tooltip("Text element for lose state (optional if nested under container)")] private TextMeshProUGUI gameOverLoseText;
		[SerializeField, Tooltip("Default number of seconds to show toast messages")]
		private int defaultToastSeconds = 3;
		[SerializeField, Tooltip("Transient toast message surface; separate from persistent status")] private TextMeshProUGUI toastText;
		[SerializeField, Tooltip("CanvasGroup for toast and its decorations - controls visibility and interactivity")] private CanvasGroup toastCanvasGroup;
		[SerializeField, Tooltip("Player names container - set active when joining match")] private GameObject playerNamesContainer;
		[SerializeField, Tooltip("Text component for local player name")] private TextMeshProUGUI localPlayerNameText;
		[SerializeField, Tooltip("Text component for opponent player name")] private TextMeshProUGUI opponentPlayerNameText;

		private System.Threading.CancellationTokenSource toastCts;
		private System.Threading.CancellationTokenSource countdownCts;
		private const int OneSecondMs = 1000;
		private static int TicksToSecondsCeil(int ticks, float tickMs)
		{
			if (ticks <= 0) return 0;
			float seconds = (ticks * tickMs) / 1000f;
			return Mathf.CeilToInt(seconds);
		}

		private void CleanupToastCts()
		{
			if (toastCts != null)
			{
				toastCts.Cancel();
				toastCts.Dispose();
				toastCts = null;
			}
		}

		private void CleanupCountdownCts()
		{
			if (countdownCts != null)
			{
				countdownCts.Cancel();
				countdownCts.Dispose();
				countdownCts = null;
			}
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			HideCountdown();
			HideStatus();
			HideGameOver();
			HideToast();
			HidePlayerNames();
		}

		public void ShowStatus(string message, string kind = "info")
		{
			if (statusText != null)
			{
				statusText.text = message;
				statusText.enabled = true;
			}
			else
			{
				Debug.Log($"[HUD][{kind}] {message}");
			}
		}

		public void HideStatus()
		{
			if (statusText != null) statusText.enabled = false;
		}

		public void ShowCountdown(int seconds)
		{
			if (countdownText != null)
			{
				countdownText.transform.parent.gameObject.SetActive(true);
				countdownText.enabled = true;
				countdownText.text = seconds.ToString();
			}
		}

		public void UpdateCountdownTimer(int seconds)
		{
			if (countdownText != null)
			{
				countdownText.text = seconds.ToString();
			}
		}

		public void HideCountdown()
		{
			if (countdownText != null) countdownText.transform.parent.gameObject.SetActive(false);
			//if (countdownText != null) countdownText.enabled = false;
		}

		public void StartCountdown(int seconds)
		{
			if (seconds <= 0)
			{
				HideCountdown();
				CleanupCountdownCts();
				return;
			}
			ShowCountdown(seconds);
			CleanupCountdownCts();
			countdownCts = new System.Threading.CancellationTokenSource();
			RunCountdownSeconds(seconds, countdownCts.Token).Forget();
		}

		public void StopCountdown()
		{
			CleanupCountdownCts();
			HideCountdown();
		}

		public void SetCountdownFromSnapshot(StateSnapshot snap, int currentServerTick, float tickMs)
		{
			int remaining = 0;
			if (snap != null)
			{
				if (snap.TryGetCountdown(out Countdown cd) && cd != null)
				{
					int endTick = cd.startsAtTick + cd.countdownTicks;
					int remainingTicks = endTick - currentServerTick;
					remaining = TicksToSecondsCeil(remainingTicks, tickMs);
				}
				else
				{
					int delta = snap.startTick - currentServerTick;
					remaining = TicksToSecondsCeil(delta, tickMs);
				}
			}
			if (remaining > 0) StartCountdown(remaining); else StopCountdown();
		}

		public void SetCountdownFromGameEvent(GameEvent evt, float tickMs)
		{
			int remaining = 0;
			if (evt != null && evt.data != null)
			{
				int delta = evt.data.startTick - evt.serverTick;
				remaining = TicksToSecondsCeil(delta, tickMs);
			}
			if (remaining > 0) StartCountdown(remaining); else StopCountdown();
		}
		[SerializeField, SoundGroup] private string countdownTickSoundName, countdownEndSoundName;
		private async UniTask RunCountdownSeconds(int seconds, System.Threading.CancellationToken token)
		{
			try
			{
				int remaining = Mathf.Max(0, seconds);
				while (remaining > 0)
				{
					await UniTask.Delay(OneSecondMs, ignoreTimeScale: true, cancellationToken: token);

					// Check cancellation immediately after delay to avoid stale UI updates
					if (token.IsCancellationRequested)
					{
						break;
					}

					remaining = Mathf.Max(0, remaining - 1);
					UpdateCountdownTimer(remaining);
					if (countdownTickSoundName != string.Empty && remaining > 0)
					{
						MasterAudio.PlaySoundAndForget(countdownTickSoundName);
					}
				}

				HideCountdown();
				if (countdownEndSoundName != string.Empty)
				{
					MasterAudio.PlaySoundAndForget(countdownEndSoundName);
				}
			}
			catch (System.OperationCanceledException)
			{
				// expected on cancellation
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[HUD] Unexpected error in RunCountdownSeconds: {ex}");
			}
		}

		public void ShowGameOver(string winnerUserId)
		{
			// Always show the root game over panel
			if (gameOverPanel != null) gameOverPanel.SetActive(true);

			// Hide player names when game is over
			HidePlayerNames();

			// Determine win/lose for the local player
			bool hasWinner = !string.IsNullOrEmpty(winnerUserId);
			bool didLocalPlayerWin = false;
			if (hasWinner && AuthManager.Instance != null && !string.IsNullOrEmpty(AuthManager.Instance.UserId))
			{
				didLocalPlayerWin = string.Equals(winnerUserId, AuthManager.Instance.UserId);
			}

			// Only use dedicated win/lose UI; never use generic text
			if (gameOverWinContainer != null) gameOverWinContainer.SetActive(hasWinner && didLocalPlayerWin);
			if (gameOverLoseContainer != null) gameOverLoseContainer.SetActive(hasWinner && !didLocalPlayerWin);
			if (gameOverWinText != null) gameOverWinText.gameObject.SetActive(hasWinner && didLocalPlayerWin);
			if (gameOverLoseText != null) gameOverLoseText.gameObject.SetActive(hasWinner && !didLocalPlayerWin);
			//if (gameOverText != null) gameOverText.enabled = false;
		}

		public void HideGameOver()
		{
			if (gameOverPanel != null) gameOverPanel.SetActive(false);
			//if (gameOverText != null) gameOverText.enabled = false;
			if (gameOverWinContainer != null) gameOverWinContainer.SetActive(false);
			if (gameOverLoseContainer != null) gameOverLoseContainer.SetActive(false);
			if (gameOverWinText != null) gameOverWinText.gameObject.SetActive(false);
			if (gameOverLoseText != null) gameOverLoseText.gameObject.SetActive(false);
		}

		public void ShowToast(string message, string kind = "info", int seconds = 0)
		{
			UniTask.Void(async () => await ShowToastAsync(message, kind, seconds));
		}

		private async UniTask ShowToastAsync(string message, string kind = "info", int seconds = 0)
		{
			if (seconds <= 0) seconds = Mathf.Max(1, defaultToastSeconds);
			if (toastText != null)
			{
				toastText.text = message;
				toastText.enabled = true;
			}
			else
			{
				Debug.Log($"[HUD][toast][{kind}] {message}");
			}

			// Show toast canvas group
			if (toastCanvasGroup != null)
			{
				toastCanvasGroup.alpha = 1f;
				toastCanvasGroup.interactable = true;
				toastCanvasGroup.blocksRaycasts = true;
			}

			// cancel any previous hide task
			CleanupToastCts();
			toastCts = new System.Threading.CancellationTokenSource();
			await HideToastAfterDelay(seconds, toastCts.Token);
		}

		private async UniTask HideToastAfterDelay(int seconds, System.Threading.CancellationToken token)
		{
			if (seconds <= 0)
			{
				HideToast();
				return;
			}

			try
			{
				await UniTask.Delay(seconds * OneSecondMs, ignoreTimeScale: true, cancellationToken: token);
				HideToast();
			}
			catch (System.OperationCanceledException)
			{
				// Expected when toast is cancelled
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[HUD] Unexpected error in HideToastAfterDelay: {ex}");
			}
		}

		public void HideToast()
		{
			// Cancel any pending hide tasks and release resources
			CleanupToastCts();

			if (toastText != null) toastText.enabled = false;

			// Hide toast canvas group
			if (toastCanvasGroup != null)
			{
				toastCanvasGroup.alpha = 0f;
				toastCanvasGroup.interactable = false;
				toastCanvasGroup.blocksRaycasts = false;
			}
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
			CleanupToastCts();
			CleanupCountdownCts();
		}

		public void ShowPlayerNames()
		{
			if (playerNamesContainer != null) playerNamesContainer.SetActive(true);
		}

		public void HidePlayerNames()
		{
			if (playerNamesContainer != null) playerNamesContainer.SetActive(false);
		}

		public void SetLocalPlayerName(string playerName)
		{
			if (localPlayerNameText != null) 
			{
				localPlayerNameText.text = string.IsNullOrEmpty(playerName) ? "Player" : playerName;
			}
		}

		public void SetOpponentPlayerName(string playerName)
		{
			if (opponentPlayerNameText != null) 
			{
				opponentPlayerNameText.text = string.IsNullOrEmpty(playerName) ? "Opponent" : playerName;
			}
		}

		/// <summary>
		/// Sets both player names and shows the UI. Call this when match starts.
		/// </summary>
		/// <param name="localPlayerName">Name of the local player</param>
		/// <param name="opponentPlayerName">Name of the opponent player</param>
		public void SetPlayerNames(string localPlayerName, string opponentPlayerName)
		{
			SetLocalPlayerName(localPlayerName);
			SetOpponentPlayerName(opponentPlayerName);
			ShowPlayerNames();
		}

		public void UpdateMana(string playerId, float mana)
		{
			// Placeholder hook; integrate with mana UI when available
			//			Debug.Log($"[HUD] ManaUpdate player={playerId} mana={mana}");
		}
	}
}


