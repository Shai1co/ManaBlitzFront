using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
	public class UnitVfxController : MonoBehaviour
	{
		private const float MinProjectileTravelSeconds = 0.001f;
		private const int MinMilliseconds = 0;

		private Unit unit;
		private UnitVfxAnchors anchors;

		private GameObject activeWindup;
		private CancellationTokenSource vfxCts;

		private void Awake()
		{
			unit = GetComponent<Unit>();
			anchors = GetComponent<UnitVfxAnchors>();
		}

		public void CancelAll()
		{
			try
			{
				vfxCts?.Cancel();
				vfxCts?.Dispose();
				vfxCts = null;
			}
			catch { }
			if (activeWindup != null)
			{
				Destroy(activeWindup);
				activeWindup = null;
			}
		}

		public void StopWindupNow()
		{
			if (activeWindup != null)
			{
				Destroy(activeWindup);
				activeWindup = null;
			}
		}

		public async UniTaskVoid HandleUseSkillVfx(UseSkillData use, int serverTick, UnitConfig unitConfig)
		{
			if (use == null || VfxManager.Instance == null || unit == null) return;
			var preset = VfxManager.Instance.GetPresetByPieceAndIndex(unit.PieceId, Mathf.Max(0, use.skillId), unitConfig);
			if (preset == null) return;

			int delayMs = 0;
			if (use.hitTick > 0 && serverTick > 0 && use.hitTick > serverTick)
			{
				int dt = use.hitTick - serverTick;
				delayMs = Mathf.Max(MinMilliseconds, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
			}

			int windupDurationMs = 0;
			if (preset.WindupPolicy == SkillVfxPreset.WindupDurationPolicy.FixedMs)
			{
				windupDurationMs = Mathf.Max(MinMilliseconds, preset.FixedWindupMs);
			}
			else
			{
				if (use.startTick > 0 && use.endWindupTick > use.startTick)
				{
					int dt = use.endWindupTick - use.startTick;
					windupDurationMs = Mathf.Max(MinMilliseconds, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
				}
				// When using FromTicks, keep windup until impact if ticks are not present
				if (windupDurationMs <= 0) windupDurationMs = delayMs;
			}

			Transform windupAnchor = ResolveSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName);
			Transform projectileAnchor = ResolveSourceAnchor(preset.ProjectileAttach, preset.CustomSourceAnchorName);

			// Determine target world position early and remember it for impact
			Vector3 targetWorldPos = transform.position;
			if (use.target != null && use.target.cell != null)
			{
				var cell = new Vector2Int(use.target.cell.x, use.target.cell.y);
				targetWorldPos = Board.Instance.GetSlotWorldPosition(cell);
			}
			VfxManager.Instance.RememberTargetPosition(unit.UnitID, targetWorldPos);

			// Prepare CTS
			vfxCts?.Cancel();
			vfxCts?.Dispose();
			vfxCts = new CancellationTokenSource();
			var linked = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), vfxCts.Token);

			// Wind-up
			if (preset.WindupPrefab != null && windupDurationMs > 0 && windupAnchor != null)
			{
				activeWindup = VfxManager.Instance.PlayAt(windupAnchor, preset.WindupPrefab);
				if (preset.AudioCast != null)
				{
					AudioSource.PlayClipAtPoint(preset.AudioCast, windupAnchor.position);
				}
				try { await UniTask.Delay(windupDurationMs, cancellationToken: linked.Token); }
				catch { }
				StopWindupNow();
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
					travelMs = Mathf.Max(MinMilliseconds, Mathf.RoundToInt(seconds * 1000f));
				}
				if (travelMs > 0)
				{
					int launchDelayMs = Mathf.Max(MinMilliseconds, delayMs - travelMs);
					await UniTask.Delay(launchDelayMs, cancellationToken: linked.Token).SuppressCancellationThrow();
					await VfxManager.Instance.PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, linked.Token);
				}
			}
		}

		private Transform ResolveSourceAnchor(SkillVfxPreset.SourceAnchor which, string customName)
		{
			if (anchors == null) return transform;
			return anchors.FindSourceAnchor(which, customName);
		}

		private Transform ResolveTargetAnchor(Unit target, SkillVfxPreset.TargetAnchor which, string customName)
		{
			if (target == null) return null;
			var ta = target.GetComponent<UnitVfxAnchors>();
			if (ta == null) return target.transform;
			return ta.FindTargetAnchor(which, customName);
		}
	}
}


