using System.Threading;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;
using UnityEngine;

namespace ManaGambit
{
	public class UnitVfxController : MonoBehaviour
	{
		private const float MinProjectileTravelSeconds = 0.001f;
		private const int MinMilliseconds = 0;
		private const float MinLookDirMagnitudeSqr = 0.0001f;

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

			// Handle movement if action has move patterns
			var unitData = unitConfig.GetData(unit.PieceId);
			if (unitData != null && unitData.actions != null && use.skillId >= 0 && use.skillId < unitData.actions.Length)
			{
				var actionInfo = unitData.actions[use.skillId];
				if (actionInfo != null && actionInfo.move != null && actionInfo.move.patterns != null && actionInfo.move.patterns.Count > 0)
				{
					await HandleMovement(use, actionInfo.move);
				}
			}

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
			UnityEngine.Vector2Int targetCell = new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y);
			if (use.target != null && use.target.cell != null)
			{
				targetCell = new UnityEngine.Vector2Int(use.target.cell.x, use.target.cell.y);
				targetWorldPos = Board.Instance.GetSlotWorldPosition(targetCell);
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
				if (preset.AudioCast != string.Empty)
				{
					MasterAudio.PlaySound3DAtVector3AndForget(preset.AudioCast, windupAnchor.position);
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
					await VfxManager.Instance.PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, linked.Token);
				}
			}

			// Handle post-impact behavior
			var unitData2 = unitConfig.GetData(unit.PieceId);
			if (unitData2 != null && unitData2.actions != null && use.skillId >= 0 && use.skillId < unitData2.actions.Length)
			{
				var actionInfo = unitData2.actions[use.skillId];
				if (actionInfo != null && actionInfo.move != null && actionInfo.move.postImpact != null)
				{
					await HandlePostImpact(actionInfo.move.postImpact, new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y), use.skillId, targetCell);
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

		private async UniTask HandleMovement(UseSkillData use, UnitConfig.MoveEffect moveEffect)
		{
			if (use == null || use.target == null || use.target.cell == null || moveEffect == null)
				return;

			// Get current and target positions
			UnityEngine.Vector2Int currentPos = new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y);
			UnityEngine.Vector2Int targetPos = new UnityEngine.Vector2Int(use.target.cell.x, use.target.cell.y);

			// Validate that the target position is actually reachable according to the pattern
			if (!UnitConfig.IsValidMovementTarget(currentPos, targetPos, moveEffect, unit.OwnerId.ToString()))
			{
				return; // Target is not valid for movement
			}

			// Calculate movement path based on patterns
			UnityEngine.Vector2Int movementTarget = UnitConfig.CalculateMovementPath(currentPos, targetPos, moveEffect, unit.OwnerId.ToString());
			// Guard: never land on an occupied target cell; prefer cell just before target along approach
			if (movementTarget == targetPos)
			{
				var dirToTarget = new UnityEngine.Vector2Int(Mathf.Clamp(targetPos.x - currentPos.x, -1, 1), Mathf.Clamp(targetPos.y - currentPos.y, -1, 1));
				var before = new UnityEngine.Vector2Int(targetPos.x - dirToTarget.x, targetPos.y - dirToTarget.y);
				if (InBounds(before.x, before.y) && !IsOccupied(before))
				{
					movementTarget = before;
				}
			}
			// Mirror JS: if stopOnHit, land just before the target along the approach direction
			if (moveEffect.patterns != null && moveEffect.patterns.Count > 0 && moveEffect.patterns[0] != null && moveEffect.patterns[0].stopOnHit)
			{
				var dir = new UnityEngine.Vector2Int(Mathf.Clamp(targetPos.x - currentPos.x, -1, 1), Mathf.Clamp(targetPos.y - currentPos.y, -1, 1));
				if (dir != UnityEngine.Vector2Int.zero)
				{
					var candidate = new UnityEngine.Vector2Int(targetPos.x - dir.x, targetPos.y - dir.y);
					while (candidate != currentPos && IsOccupied(candidate))
					{
						candidate = new UnityEngine.Vector2Int(candidate.x - dir.x, candidate.y - dir.y);
					}
					if (candidate != currentPos)
					{
						movementTarget = candidate;
					}
				}
			}

			// If movement target is different from current position, move the unit
			if (movementTarget != currentPos)
			{
				// Face the movement direction (Y rotation only)
				var board = Board.Instance;
				if (board != null)
				{
					Vector3 startWorld = board.GetSlotWorldPosition(currentPos);
					Vector3 targetWorldCenter = board.GetSlotWorldPosition(movementTarget);
					Vector3 lookDelta = targetWorldCenter - startWorld;
					lookDelta.y = 0f;
					if (lookDelta.sqrMagnitude > MinLookDirMagnitudeSqr)
					{
						float yaw = Mathf.Atan2(lookDelta.x, lookDelta.z) * Mathf.Rad2Deg;
						var e = unit.transform.eulerAngles;
						e.y = yaw;
						unit.transform.eulerAngles = e;
					}
				}
				// Use the public MoveTo method which properly updates position and handles animation.
				// Apply an attack offset if defined in the preset for this skill.
				var preset = VfxManager.Instance.GetPresetByPieceAndIndex(unit.PieceId, Mathf.Max(0, use.skillId), NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null);
				float attackOffset = preset != null ? Mathf.Max(0f, preset.AttackMoveOffsetWorldUnits) : 0f;
				if (attackOffset <= 0f)
				{
					await unit.MoveTo(movementTarget);
				}
				else
				{
					// Move to the tile but stop short by offset along the movement direction
					var board2 = Board.Instance;
					if (board2 == null)
					{
						await unit.MoveTo(movementTarget);
					}
					else
					{
						Vector3 startWorld = board2.GetSlotWorldPosition(currentPos);
						Vector3 targetWorldCenter = board2.GetSlotWorldPosition(movementTarget);
						Vector3 delta = targetWorldCenter - startWorld;
						delta.y = 0f;
						Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
						Vector3 adjusted = targetWorldCenter - (dir * attackOffset);
						// Temporarily teleport using transform for offset, then update logical coord to the target tile
						await unit.MoveTo(movementTarget);
						unit.transform.position = adjusted;
					}
				}

				// Handle post-impact behavior
				if (moveEffect.postImpact != null)
				{
					await HandlePostImpact(moveEffect.postImpact, currentPos, use.skillId, targetPos);
				}
				else
				{
					TransitionToIdle();
				}
			}
		}

		private float EstimateTileWorldSize(Board board, Vector2Int coord)
		{
			if (board == null) return 1f;
			var right = new Vector2Int(Mathf.Min(Board.Size - 1, coord.x + 1), coord.y);
			var up = new Vector2Int(coord.x, Mathf.Min(Board.Size - 1, coord.y + 1));
			Vector3 c = board.GetSlotWorldPosition(coord);
			Vector3 r = board.GetSlotWorldPosition(right);
			Vector3 u = board.GetSlotWorldPosition(up);
			float sx = Mathf.Max(0.001f, Vector3.Distance(c, r));
			float sy = Mathf.Max(0.001f, Vector3.Distance(c, u));
			return Mathf.Max(sx, sy);
		}

		private async UniTask HandlePostImpact(UnitConfig.PostImpact postImpact, UnityEngine.Vector2Int originalPos, int skillIndex, UnityEngine.Vector2Int targetCell)
		{
			if (postImpact == null) return;

			string behaviorKey = postImpact.behavior;
			if (!string.IsNullOrEmpty(behaviorKey)) behaviorKey = behaviorKey.Replace(" ", string.Empty).ToLowerInvariant();
			switch (behaviorKey)
			{
				case "returnhome":
					// Play return-home animation clip if available during the return movement
					var ua = unit.GetComponent<UnitAnimator>();
					if (ua != null)
					{
						ua.SetSkillIndex(Mathf.Max(0, skillIndex));
						ua.PlayReturnHomeState(Mathf.Max(0, skillIndex));
					}
					await unit.MoveTo(originalPos);
					TransitionToIdle();
					break;

				case "landneartarget":
				{
					int radius = Mathf.Max(1, postImpact.radius);
					bool pickRandom = postImpact.random;
					var board = Board.Instance;
					if (board == null) break;
					UnityEngine.Vector2Int? chosen = FindLandNearTargetCell(targetCell, radius, pickRandom);
					if (chosen.HasValue)
					{
						await unit.MoveTo(chosen.Value);
					}
					// After landing, return to idle
					TransitionToIdle();
					break;
				}

				default:
					// Default: ensure we return to idle
					TransitionToIdle();
					break;
			}
		}

		private void TransitionToIdle(float crossfadeDuration = 0.15f)
		{
			var unitAnimator = unit.GetComponent<UnitAnimator>();
			if (unitAnimator != null)
			{
				unitAnimator.CrossfadeToIdle(crossfadeDuration);
				unitAnimator.ClearSkillIndex();
			}
		}

		private UnityEngine.Vector2Int? FindLandNearTargetCell(UnityEngine.Vector2Int target, int radius, bool random)
		{
			// Build all candidate cells within Chebyshev distance [1..radius]
			radius = Mathf.Max(1, radius);
			System.Collections.Generic.List<UnityEngine.Vector2Int> candidates = new System.Collections.Generic.List<UnityEngine.Vector2Int>(radius * radius * 8);
			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (dx == 0 && dy == 0) continue; // exclude target itself
					int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
					if (cheb < 1 || cheb > radius) continue;
					int x = target.x + dx;
					int y = target.y + dy;
					if (!InBounds(x, y)) continue;
					var cell = new UnityEngine.Vector2Int(x, y);
					if (!IsOccupied(cell)) candidates.Add(cell);
				}
			}
			if (candidates.Count == 0) return null;
			if (random)
			{
				for (int i = 0; i < candidates.Count; i++)
				{
					int j = UnityEngine.Random.Range(i, candidates.Count);
					(var a, var b) = (candidates[i], candidates[j]);
					candidates[i] = b; candidates[j] = a;
				}
				return candidates[0];
			}
			else
			{
				// Prefer the immediate cell before target along approach vector if available
				var from = new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y);
				var dir = new UnityEngine.Vector2Int(Mathf.Clamp(target.x - from.x, -1, 1), Mathf.Clamp(target.y - from.y, -1, 1));
				if (dir != UnityEngine.Vector2Int.zero)
				{
					var immediate = new UnityEngine.Vector2Int(target.x - dir.x, target.y - dir.y);
					for (int i = 0; i < candidates.Count; i++) { if (candidates[i] == immediate) return immediate; }
				}
				// Fallback: choose closest-by-distance to target
				int bestIdx = 0; int bestCheb = 9999;
				for (int i = 0; i < candidates.Count; i++)
				{
					var c = candidates[i];
					int cheb = Mathf.Max(Mathf.Abs(c.x - target.x), Mathf.Abs(c.y - target.y));
					if (cheb < bestCheb) { bestCheb = cheb; bestIdx = i; }
				}
				return candidates[bestIdx];
			}
		}

		private static bool InBounds(int x, int y)
		{
			return x >= 0 && x < Board.Size && y >= 0 && y < Board.Size;
		}

		private static bool IsOccupied(UnityEngine.Vector2Int pos)
		{
			if (GameManager.Instance == null || Board.Instance == null) return false;
			var allUnits = GameManager.Instance.GetAllUnits();
			for (int i = 0; i < allUnits.Count; i++)
			{
				var u = allUnits[i];
				if (u == null) continue;
				UnityEngine.Vector2Int up;
				try { if (Board.Instance.TryGetCoord(u, out up) && up == pos) return true; }
				catch { }
			}
			return false;
		}
	}
}


