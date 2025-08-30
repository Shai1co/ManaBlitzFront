using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ManaGambit
{
	public class SkillButtonUI : MonoBehaviour
	{
		[SerializeField] private Button button;
		[SerializeField] private Image iconImage;
		[SerializeField] private TextMeshProUGUI nameText;
		[SerializeField] private TextMeshProUGUI manaText;
		[SerializeField] private TextMeshProUGUI cooldownText;

		private Unit boundUnit;
		private int actionIndex;
		private ManaGambit.ClickInput _clickInput;

		public void Setup(Unit unit, int index, UnitConfig.ActionInfo info)
		{
			boundUnit = unit;
			actionIndex = index;
			if (nameText != null) nameText.text = string.IsNullOrEmpty(info.shortDisplayName) ? info.name : info.shortDisplayName;
			if (manaText != null) manaText.text = info.manaCost > 0 ? info.manaCost.ToString() : "";
			if (cooldownText != null) cooldownText.text = info.cooldownMs > 0 ? Mathf.CeilToInt(info.cooldownMs / 1000f).ToString() + "s" : "";
			// Icon assignment left to caller or via addressables; keep empty if not set
			if (button != null) button.onClick.AddListener(OnClick);
		}

		private void OnDestroy()
		{
			if (button != null) button.onClick.RemoveListener(OnClick);
		}

		private void OnClick()
		{
			if (boundUnit == null) return;
			var input = _clickInput;
			if (input != null && input.BeginSkillTargeting(boundUnit, actionIndex)) return;
			// Fallback: send immediately if input targeting not available
			if (IntentManager.Instance != null && !string.IsNullOrEmpty(boundUnit.UnitID))
			{
				var target = new SkillTarget();
				_ = IntentManager.Instance.SendUseSkillIntent(boundUnit.UnitID, actionIndex, target);
			}
		}

		private void Awake()
		{
			_clickInput = FindFirstObjectByType<ManaGambit.ClickInput>();
		}
	}
}



