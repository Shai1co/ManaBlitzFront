using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ManaGambit
{
	public class ManaBarUI : MonoBehaviour
	{
		public static ManaBarUI Instance { get; private set; }

		public event Action<int> OnManaChanged;

		[SerializeField] private Slider manaSlider;
		[SerializeField] private TextMeshProUGUI manaText;
		[Min(0)][SerializeField] private int manaMaxPips = 10;
		[Min(0)][SerializeField] private int initialPips = 0;
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
				manaSlider.wholeNumbers = true;
			}
			// Initialize from a serialized starting value instead of whatever the Slider has in the scene
			int clampedValue = Mathf.RoundToInt(Mathf.Clamp(initialPips, 0, manaMaxPips));
			if (manaSlider != null) manaSlider.SetValueWithoutNotify(clampedValue);
			currentPips = clampedValue;
			UpdateText(clampedValue);
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

			// Invoke each subscriber individually so one exception can't halt all handlers,
			// and log any errors for easier debugging.
			var handlers = OnManaChanged;
			if (handlers != null)
			{
				foreach (var d in handlers.GetInvocationList())
				{
					try
					{
						((Action<int>)d)(currentPips);
					}
					catch (Exception ex)
					{
						Debug.LogError($"OnManaChanged handler threw: {ex}");
					}
				}
			}
		}
		public int CurrentPips => currentPips;

		public void SetMaxPips(int max)
		{
			manaMaxPips = Mathf.Max(0, max);
			if (manaSlider != null)
			{
				manaSlider.minValue = 0;
				manaSlider.maxValue = manaMaxPips;
				manaSlider.wholeNumbers = true;
				manaSlider.SetValueWithoutNotify(Mathf.Clamp(manaSlider.value, 0, manaMaxPips));
			}
			currentPips = Mathf.Clamp(currentPips, 0, manaMaxPips);
			UpdateText(currentPips);
		}

		private void UpdateText(int current)
		{
			if (manaText != null)
			{
				manaText.text = $"{current} <color=#ffa3ef>/ {manaMaxPips}</color>";
				manaText.enabled = true;
			}
		}

		private void OnValidate()
		{
			manaMaxPips = Mathf.Max(0, manaMaxPips);
			if (manaSlider != null)
			{
				manaSlider.minValue = 0;
				manaSlider.maxValue = manaMaxPips;
				manaSlider.wholeNumbers = true;
				manaSlider.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.Clamp(manaSlider.value, 0, manaMaxPips)));
			}
			currentPips = Mathf.Clamp(currentPips, 0, manaMaxPips);
			UpdateText(currentPips);
		}

		private void OnDestroy()
		{
			// Clear the instance reference when this singleton is destroyed
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}


