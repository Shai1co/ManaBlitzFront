using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
	public class VfxManager : MonoBehaviour
	{
		private const float MinProjectileTravelSeconds = 0.001f;
		public static VfxManager Instance { get; private set; }

		[SerializeField] private SkillVfxDatabase database;

		// Remember last intended target position per attacker (unitId)
		private readonly Dictionary<string, Vector3> attackerIdToTargetPos = new Dictionary<string, Vector3>();
		private readonly Dictionary<string, GameObject> attackerIdToWindup = new Dictionary<string, GameObject>();
		private readonly Dictionary<string, CancellationTokenSource> attackerIdToCts = new Dictionary<string, CancellationTokenSource>();

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		public SkillVfxPreset GetPresetByActionName(string actionName)
		{
			return database != null ? database.GetByActionName(actionName) : null;
		}

		public SkillVfxPreset GetPresetByPieceAndIndex(string pieceId, int actionIndex, UnitConfig unitConfig)
		{
			if (unitConfig == null) return null;
			var actionName = unitConfig.GetActionName(pieceId, actionIndex);
			if (string.IsNullOrEmpty(actionName)) return null;
			return GetPresetByActionName(actionName);
		}

		public GameObject PlayAt(Transform parent, GameObject prefab)
		{
			if (prefab == null || parent == null) return null;
			var go = Instantiate(prefab, parent.position, parent.rotation, parent);
			return go;
		}

		public GameObject PlayAt(Vector3 position, Quaternion rotation, GameObject prefab)
		{
			if (prefab == null) return null;
			var go = Instantiate(prefab, position, rotation);
			return go;
		}

		public async UniTask PlayProjectile(Transform source, Vector3 targetPos, GameObject projectilePrefab, int travelMs, CancellationToken token)
		{
			if (projectilePrefab == null || source == null) return;
			Vector3 direction = (targetPos - source.position).normalized;
			Quaternion rotation = direction == Vector3.zero
			    ? Quaternion.identity
			    : Quaternion.LookRotation(direction);
			var proj = Instantiate(projectilePrefab, source.position, rotation);
			float travelSeconds = Mathf.Max(MinProjectileTravelSeconds, travelMs / 1000f);
			Vector3 startPos = proj.transform.position;
			float elapsed = 0f;
			while (elapsed < travelSeconds)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, token);
				if (proj == null || proj.transform == null) break;
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / travelSeconds);
				proj.transform.position = Vector3.Lerp(startPos, targetPos, t);
			}
			if (proj != null) Destroy(proj);
		}

		public void OnUseSkill(UseSkillData use, int serverTick)
		{
			if (use == null || GameManager.Instance == null) return;
			var attacker = GameManager.Instance.GetUnitById(use.unitId);
			if (attacker == null) return;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var preset = cfg != null ? GetPresetByPieceAndIndex(attacker.PieceId, Mathf.Max(0, use.skillId), cfg) : null;
			if (preset == null) return;

			int delayMs = 0;
			if (use.hitTick > 0 && serverTick > 0 && use.hitTick > serverTick)
			{
				int dt = use.hitTick - serverTick;
				delayMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
			}

			int windupDurationMs = 0;
			if (preset.WindupPolicy == SkillVfxPreset.WindupDurationPolicy.FixedMs)
			{
				windupDurationMs = Mathf.Max(0, preset.FixedWindupMs);
			}
			else
			{
				if (use.startTick > 0 && use.endWindupTick > use.startTick)
				{
					int dt = use.endWindupTick - use.startTick;
					windupDurationMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
				}
				if (windupDurationMs <= 0) windupDurationMs = delayMs;
			}

			var attackerAnchors = attacker.GetComponent<UnitVfxAnchors>();
			Transform windupAnchor = attackerAnchors != null
				? attackerAnchors.FindSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName)
				: attacker.transform;
			Transform projectileAnchor = attackerAnchors != null
				? attackerAnchors.FindSourceAnchor(preset.ProjectileAttach, preset.CustomSourceAnchorName)
				: attacker.transform;

			Vector3 targetWorldPos = attacker.transform.position;
			if (use.target != null && use.target.cell != null)
			{
				var cell = new Vector2Int(use.target.cell.x, use.target.cell.y);
				targetWorldPos = Board.Instance.GetSlotWorldPosition(cell);
			}
			RememberTargetPosition(attacker.UnitID, targetWorldPos);

			if (attackerIdToCts.TryGetValue(attacker.UnitID, out var existingCts))
			{
				existingCts.Cancel();
				existingCts.Dispose();
				attackerIdToCts.Remove(attacker.UnitID);
			}
			var cts = new CancellationTokenSource();
			attackerIdToCts[attacker.UnitID] = cts;
			var linked = CancellationTokenSource.CreateLinkedTokenSource(attacker.GetCancellationTokenOnDestroy(), cts.Token);

			RunWindupAndProjectile(attacker.UnitID, preset, windupAnchor, projectileAnchor, windupDurationMs, delayMs, targetWorldPos, linked.Token).Forget();
		}

		private async UniTaskVoid RunWindupAndProjectile(
			string attackerUnitId,
			SkillVfxPreset preset,
			Transform windupAnchor,
			Transform projectileAnchor,
			int windupDurationMs,
			int delayMs,
			Vector3 targetWorldPos,
			CancellationToken token)
		{
			// Wind-up
			if (preset.WindupPrefab != null && windupDurationMs > 0 && windupAnchor != null)
			{
				var go = PlayAt(windupAnchor, preset.WindupPrefab);
				attackerIdToWindup[attackerUnitId] = go;
				if (preset.AudioCast != null)
				{
					AudioSource.PlayClipAtPoint(preset.AudioCast, windupAnchor.position);
				}
				try { await UniTask.Delay(windupDurationMs, cancellationToken: token); }
				catch { }
				StopWindup(attackerUnitId);
			}

			// Projectile
			if (preset.ProjectilePrefab != null && projectileAnchor != null && delayMs > 0)
			{
				int travelMs = 0;
				if (preset.TravelMs > 0)
				{
					travelMs = preset.TravelMs;
				}
				else if (preset.ProjectileSpeedUnitsPerSec > 0f)
				{
					float distance = Vector3.Distance(projectileAnchor.position, targetWorldPos);
					float seconds = Mathf.Max(MinProjectileTravelSeconds, distance / preset.ProjectileSpeedUnitsPerSec);
					travelMs = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));
				}
				if (travelMs > 0)
				{
					int launchDelayMs = Mathf.Max(0, delayMs - travelMs);
					await UniTask.Delay(launchDelayMs, cancellationToken: token).SuppressCancellationThrow();
					await PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, token);
				}
			}
		}

		private void StopWindup(string attackerUnitId)
		{
			if (attackerIdToWindup.TryGetValue(attackerUnitId, out var go))
			{
				if (go != null) Destroy(go);
				attackerIdToWindup.Remove(attackerUnitId);
			}
		}

		public void OnUseSkillResult(UseSkillResultData res, int serverTick)
		{
			if (res == null || GameManager.Instance == null) return;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var attacker = GameManager.Instance.GetUnitById(res.attacker);
			SkillVfxPreset preset = null;
			if (attacker != null && cfg != null)
			{
				preset = GetPresetByPieceAndIndex(attacker.PieceId, Mathf.Max(0, res.skillId), cfg);
			}
			if (preset == null || preset.ImpactPrefab == null) return;

			Vector3 rememberedTargetPos;
			bool haveRemembered = TryGetRememberedTargetPosition(res.attacker, out rememberedTargetPos);

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
					SpawnImpact(preset, attacker, targetUnit, fallback);
				}
			}
		}

		public void RememberTargetPosition(string attackerUnitId, Vector3 worldPos)
		{
			if (string.IsNullOrEmpty(attackerUnitId)) return;
			attackerIdToTargetPos[attackerUnitId] = worldPos;
		}

		public bool TryGetRememberedTargetPosition(string attackerUnitId, out Vector3 worldPos)
		{
			if (!string.IsNullOrEmpty(attackerUnitId) && attackerIdToTargetPos.TryGetValue(attackerUnitId, out worldPos))
			{
				return true;
			}
			worldPos = Vector3.zero;
			return false;
		}

		public void SpawnImpact(SkillVfxPreset preset, Unit attacker, Unit target, Vector3 targetWorldFallback)
		{
			if (preset == null || preset.ImpactPrefab == null) return;
			// Decide spawn transform
			switch (preset.ImpactAttach)
			{
				case SkillVfxPreset.TargetAnchor.TargetRoot:
				{
					var t = target != null ? target.transform : null;
					if (t != null) PlayAt(t, preset.ImpactPrefab);
					else PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab);
					break;
				}
				case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
				{
					Transform anchor = null;
					if (target != null)
					{
						var ta = target.GetComponent<UnitVfxAnchors>();
						if (ta != null)
						{
							anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.TargetImpactAnchor, preset.CustomTargetAnchorName);
						}
					}
					if (anchor != null) PlayAt(anchor, preset.ImpactPrefab);
					else PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab);
					break;
				}
				case SkillVfxPreset.TargetAnchor.Custom:
				{
					Transform anchor = null;
					if (target != null)
					{
						var ta = target.GetComponent<UnitVfxAnchors>();
						if (ta != null)
						{
							anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.Custom, preset.CustomTargetAnchorName);
						}
					}
					if (anchor != null) PlayAt(anchor, preset.ImpactPrefab);
					else PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab);
					break;
				}
				case SkillVfxPreset.TargetAnchor.WorldAtTargetCell:
				default:
				{
					PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab);
					break;
				}
			}
			if (preset.AudioImpact != null)
			{
				AudioSource.PlayClipAtPoint(preset.AudioImpact, targetWorldFallback);
			}
		}
	}
}


