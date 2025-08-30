using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ManaGambit
{
	public class ManaBarUI : MonoBehaviour
	{
		public static ManaBarUI Instance { get; private set; }

		public static event Action<int> OnManaChanged;

		[SerializeField] private Slider manaSlider;
		[SerializeField] private TextMeshProUGUI manaText;
		[SerializeField] private int manaMaxPips = 10;
		private int currentPips;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			if (manaSlider != null)
			{
				manaSlider.minValue = 0;
				manaSlider.maxValue = manaMaxPips;
			}
			currentPips = manaSlider != null ? (int)manaSlider.value : 0;
			UpdateText(currentPips);
		}

		public void SetMana(float mana)
		{
			int clamped = Mathf.Clamp(Mathf.RoundToInt(mana), 0, manaMaxPips);
			if (manaSlider != null)
			{
				if (manaSlider.maxValue != manaMaxPips)
				{
					manaSlider.minValue = 0;
					manaSlider.maxValue = manaMaxPips;
				}
				manaSlider.value = clamped;
			}
			currentPips = clamped;
			UpdateText(clamped);
			try { OnManaChanged?.Invoke(currentPips); } catch { }
		}

		public int CurrentPips => currentPips;

		private void UpdateText(int current)
		{
			if (manaText != null)
			{
				manaText.text = current.ToString() + " <color=#ffa3ef>/ " + manaMaxPips.ToString() + "</color>";
				manaText.enabled = true;
			}
		}
	}
}


