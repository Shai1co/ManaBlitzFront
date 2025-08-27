using UnityEngine;
using UnityEngine.UI;

namespace ManaGambit
{
	public class SkillBarUI : MonoBehaviour, ISkillBarUI
	{
		[SerializeField] private Transform container;
		[SerializeField] private SkillButtonUI buttonPrefab;
		[SerializeField] private UnitConfig unitConfig;

		private Unit boundUnit;

		public void BindUnit(Unit unit)
		{
			boundUnit = unit;
			Refresh();
		}

		public void Clear()
		{
			boundUnit = null;
			for (int i = container.childCount - 1; i >= 0; i--)
			{
				Destroy(container.GetChild(i).gameObject);
			}
		}

		private void Refresh()
		{
			Clear();
			if (boundUnit == null || unitConfig == null) return;
			var data = unitConfig.GetData(boundUnit.PieceId);
			if (data == null || data.actions == null) return;
			for (int i = 0; i < data.actions.Length; i++)
			{
				var action = data.actions[i];
				if (action == null) continue;
				var btn = Instantiate(buttonPrefab, container);
				btn.Setup(boundUnit, i, action);
			}
		}
	}
}


