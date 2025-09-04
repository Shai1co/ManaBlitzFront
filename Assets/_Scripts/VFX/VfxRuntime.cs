using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
	public static class VfxRuntime
	{
		public static void OnUseSkill(UseSkillData use, int serverTick)
		{
			if (use == null || GameManager.Instance == null) return;
			var attacker = GameManager.Instance.GetUnitById(use.unitId);
			if (attacker == null) return;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var ctrl = attacker.GetComponent<UnitVfxController>();
			if (ctrl == null) ctrl = attacker.gameObject.AddComponent<UnitVfxController>();
			ctrl.HandleUseSkillVfx(use, serverTick, cfg).Forget();
		}

		public static void OnUseSkillResult(UseSkillResultData res, int serverTick)
		{
			if (res == null || GameManager.Instance == null || VfxManager.Instance == null) return;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var attacker = GameManager.Instance.GetUnitById(res.attacker);
			SkillVfxPreset preset = null;
			if (attacker != null && cfg != null)
			{
				preset = VfxManager.Instance.GetPresetByPieceAndIndex(attacker.PieceId, Mathf.Max(0, res.skillId), cfg);
			}
			if (preset == null || preset.ImpactPrefab == null) return;

			// Optionally ensure impact happens close to hitTick: if serverTick < hitTick, we could delay. Keep it immediate for responsiveness.
			Vector3 rememberedTargetPos;
			bool haveRemembered = VfxManager.Instance.TryGetRememberedTargetPosition(res.attacker, out rememberedTargetPos);

			if (res.targets != null)
			{
				for (int i = 0; i < res.targets.Length; i++)
				{
					var t = res.targets[i];
					if (t == null) continue;
					var targetUnit = !string.IsNullOrEmpty(t.unitId) ? GameManager.Instance.GetUnitById(t.unitId) : null;
					Vector3 fallback = targetUnit != null
						? targetUnit.transform.position
						: (haveRemembered ? rememberedTargetPos : attacker != null ? attacker.transform.position : Vector3.zero);
					VfxManager.Instance.SpawnImpact(preset, attacker, targetUnit, fallback);
				}
			}
		}
	}
}


