using UnityEngine;
using UnityEngine.UI;

namespace ManaGambit
{
	public class HudController : MonoBehaviour
	{
		public static HudController Instance { get; private set; }

		[SerializeField] private Text statusText;
		[SerializeField] private Text countdownText;
		[SerializeField] private GameObject gameOverPanel;
		[SerializeField] private Text gameOverText;

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

		public void UpdateMana(string playerId, float mana)
		{
			// Placeholder hook; integrate with mana UI when available
			Debug.Log($"[HUD] ManaUpdate player={playerId} mana={mana}");
		}
	}
}


