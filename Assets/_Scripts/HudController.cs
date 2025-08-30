using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
	public class HudController : MonoBehaviour
	{
		public static HudController Instance { get; private set; }

		[SerializeField] private TextMeshProUGUI statusText;
		[SerializeField] private TextMeshProUGUI countdownText;
		[SerializeField] private GameObject gameOverPanel;
		[SerializeField] private TextMeshProUGUI gameOverText;
		[SerializeField, Tooltip("Default number of seconds to show toast messages")]
		private int defaultToastSeconds = 3;
		[SerializeField, Tooltip("Transient toast message surface; separate from persistent status")] private TextMeshProUGUI toastText;

		private System.Threading.CancellationTokenSource toastCts;

		private void CleanupToastCts()
		{
			if (toastCts != null)
			{
				toastCts.Cancel();
				toastCts.Dispose();
				toastCts = null;
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
			if (countdownText != null) countdownText.enabled = false;
		}

		public void ShowGameOver(string winnerUserId)
		{
			if (gameOverPanel != null) gameOverPanel.SetActive(true);
			if (gameOverText != null)
			{
				gameOverText.text = string.IsNullOrEmpty(winnerUserId) ? "Game Over" : ($"Winner: {winnerUserId}");
			}
			else
			{
				Debug.Log($"[HUD] Game Over. Winner: {winnerUserId}");
			}
		}

		public void HideGameOver()
		{
			if (gameOverPanel != null) gameOverPanel.SetActive(false);
		}

		public void ShowToast(string message, string kind = "info", int seconds = 0)
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
			// cancel any previous hide task
			CleanupToastCts();
			toastCts = new System.Threading.CancellationTokenSource();
			HideToastAfterDelay(seconds, toastCts.Token).Forget();
		}

		private async UniTaskVoid HideToastAfterDelay(int seconds, System.Threading.CancellationToken token)
		{
			try
			{
				await UniTask.Delay(seconds * 1000, cancellationToken: token);
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
			if (toastText != null) toastText.enabled = false;
		}

		private void OnDestroy()
		{
			CleanupToastCts();
		}

		public void UpdateMana(string playerId, float mana)
		{
			// Placeholder hook; integrate with mana UI when available
			Debug.Log($"[HUD] ManaUpdate player={playerId} mana={mana}");
		}
	}
}


