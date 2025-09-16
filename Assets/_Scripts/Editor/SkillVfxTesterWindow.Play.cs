using UnityEditor;
using UnityEngine;

namespace ManaGambit
{
	public partial class SkillVfxTesterWindow
	{
		private const int PostImpactReturnDelayMs = 500;
		private const int EditorReturnDurationMs = 300;
		private const int EditorIdleCrossfadeMs = 250;
		private void PlayVfx()
		{
			if (attacker == null)
			{
				ShowNotification(new GUIContent("Assign an attacker"));
				return;
			}
			if (VfxManager.Instance == null)
			{
				ShowNotification(new GUIContent("Missing VfxManager in scene"));
				return;
			}
			if (unitConfig == null)
			{
				ShowNotification(new GUIContent("Assign UnitConfig"));
				return;
			}

			int idx = GetResolvedSkillIndex();
			var preset = VfxManager.Instance.GetPresetByPieceAndIndex(attacker.PieceId, idx, unitConfig);
			if (preset == null)
			{
				ShowNotification(new GUIContent("No preset for this action"));
				return;
			}

			// Ensure preset's config speed is up to date for this action
			int cfgSpeed = unitConfig.GetAttackSpeed(attacker.PieceId, idx);
			if (cfgSpeed > 0)
			{
				preset.EditorSetProjectileSpeedFromConfig(cfgSpeed);
			}

			// Check if this action has movement patterns and handle movement
			var unitData = unitConfig.GetData(attacker.PieceId);
			UnitConfig.MoveEffect moveEffect = null;
			if (unitData != null && unitData.actions != null && idx >= 0 && idx < unitData.actions.Length)
			{
				var actionInfo = unitData.actions[idx];
				if (actionInfo != null)
				{
					moveEffect = actionInfo.move;
				}
			}

			var unitAnim = attacker.GetComponent<UnitAnimator>();
			int windMs = overrideWindupMs ? Mathf.Max(0, windupMsOverride) : unitConfig.GetWindUpMs(attacker.PieceId, idx);

			// Optionally invoke UnitSkillEvents (can be disabled to avoid duplicate side-effects in tester)
			if (invokeUnitEvents)
			{
				try { EditorInvokeSkillStart(attacker, idx); } catch { }
			}

			// Ensure board occupancy/coords are synced to nearest slots for editor correctness
			EditorSyncUnitToNearestBoardSlot(attacker);
			if (useTargetUnit && target != null) EditorSyncUnitToNearestBoardSlot(target);

			// Handle movement if action has move patterns
			if (moveEffect != null && moveEffect.patterns != null && moveEffect.patterns.Count > 0)
			{
				HandleMovementForVfx(moveEffect, windMs, preset != null ? preset.AttackMoveOffsetWorldUnits : 0f);
			}
			if (!Application.isPlaying && unitAnim != null)
			{
				unitAnim.SetSkillIndex(idx);
				unitAnim.SetState(UnitState.Attacking);
				int runMs = windMs > 0 ? (windMs + EditorAnimPaddingMs) : (Mathf.Max(1, Mathf.RoundToInt(unitAnim.GetCurrentStateLengthSeconds() * 1000f)) + EditorOneShotPaddingMs);
				StartEditorAnimTick(unitAnim, runMs);
				// Also schedule a crossfade back to Idle after the clip duration
				int remaining = Mathf.Max(50, Mathf.RoundToInt(unitAnim.GetRemainingTimeSecondsForCurrentStateOneShot() * 1000f));
				EditorSchedule(() => { unitAnim.CrossfadeToIdle(0.15f); unitAnim.ClearSkillIndex(); StartEditorAnimTick(unitAnim, EditorIdleCrossfadeMs); }, remaining + 25);
			}

			var anchors = attacker.GetComponent<UnitVfxAnchors>();
			var windAnchor = anchors != null ? anchors.FindSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName) : attacker.transform;
			var projAnchor = anchors != null ? anchors.FindSourceAnchor(preset.ProjectileAttach, preset.CustomSourceAnchorName) : attacker.transform;
			Vector3 targetPos = ResolveEditorProjectileTargetWorldPos(preset);
			VfxManager.Instance.RememberTargetPosition(attacker.UnitID, targetPos);
			// Remember original position to support post-impact behaviors like ReturnHome
			var originalPosForPostImpact = GetBoardCoord(attacker);

			// Face target on Y only immediately in editor; we'll restore after impact
			float originalYaw = attacker.transform.eulerAngles.y;
			{
				Vector3 lookDelta = targetPos - attacker.transform.position;
				lookDelta.y = 0f;
				if (lookDelta.sqrMagnitude > 0.0001f)
				{
					float yaw = Mathf.Atan2(lookDelta.x, lookDelta.z) * Mathf.Rad2Deg;
					var e = attacker.transform.eulerAngles;
					e.y = yaw;
					attacker.transform.eulerAngles = e;
				}
			}

			if (preset.WindupPrefab != null && windMs > 0)
			{
				VfxManager.Instance.PlayAtEditor(windAnchor, preset.WindupPrefab, windMs);
			}

			// Aura preview in editor: if preset has aura and UnitConfig action defines duration
			int auraDurationMsEditor = 0;
			{
				var data = unitConfig.GetData(attacker.PieceId);
				int aidx = GetResolvedSkillIndex();
				if (data != null && data.actions != null && aidx >= 0 && aidx < data.actions.Length)
				{
					var actionInfo = data.actions[aidx];
					if (actionInfo != null && actionInfo.aura != null)
					{
						auraDurationMsEditor = Mathf.Max(0, actionInfo.aura.duration);
					}
				}
			}
			if (preset.AuraPrefab != null && auraDurationMsEditor > 0)
			{
				var auraAnchor = anchors != null ? anchors.FindSourceAnchor(preset.AuraAttach, preset.CustomSourceAnchorName) : attacker.transform;
				VfxManager.Instance.PlayAtEditor(auraAnchor, preset.AuraPrefab, auraDurationMsEditor, preset.AuraLookAtCamera);
			}

			// Resolve attack multi-shot values (defaults to single shot)
			int numShots = 1;
			int shotIntervalMs = 0;
			{
				var data = unitConfig.GetData(attacker.PieceId);
				int aidx = GetResolvedSkillIndex();
				if (data != null && data.actions != null && aidx >= 0 && aidx < data.actions.Length)
				{
					var actionInfo = data.actions[aidx];
					if (actionInfo != null && actionInfo.attack != null)
					{
						numShots = Mathf.Max(1, actionInfo.attack.amount);
						shotIntervalMs = Mathf.Max(0, actionInfo.attack.interval);
					}
				}
			}
			Debug.Log($"[VFX Tester] Shots: numShots={numShots}, intervalMs={shotIntervalMs}, hasProjectile={(preset.ProjectilePrefab!=null)}, invokeUnitEvents={invokeUnitEvents}");

			bool hasProjectile = preset.ProjectilePrefab != null;
			if (hasProjectile)
			{
				int projectileTravelMs = 0;
				if (preset.TravelMs > 0)
				{
					projectileTravelMs = preset.TravelMs;
				}
				else
				{
					float speed = preset.ProjectileSpeedFromConfigUnitsPerSec > 0f ? preset.ProjectileSpeedFromConfigUnitsPerSec : preset.ProjectileSpeedUnitsPerSec;
					if (speed > 0f)
					{
						float dist = Vector3.Distance(projAnchor.position, targetPos);
						projectileTravelMs = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0.001f, dist / speed) * 1000f));
					}
				}

				// Schedule each shot
				for (int s = 0; s < numShots; s++)
				{
					int shotIndex = s;
					int baseDelay = windMs + (shotIndex * shotIntervalMs);
					Debug.Log($"[VFX Tester] Schedule projectile shot {shotIndex+1}/{numShots} at +{baseDelay}ms, travelMs={projectileTravelMs}");
					EditorSchedule(() =>
					{
						// Replay skill animation for additional shots in Edit Mode
						if (unitAnim != null && shotIndex > 0)
						{
							unitAnim.SetSkillIndex(idx);
							unitAnim.SetState(UnitState.Attacking);
							int animMs = Mathf.Max(1, Mathf.RoundToInt(unitAnim.GetCurrentStateLengthSeconds() * 1000f)) + EditorOneShotPaddingMs;
							StartEditorAnimTick(unitAnim, animMs);
						}
						VfxManager.Instance.PlayProjectileEditor(projAnchor, targetPos, preset.ProjectilePrefab, projectileTravelMs, 0, preset.ProjectileArcDegrees);
						EditorSchedule(() =>
						{
							Debug.Log($"[VFX Tester] SpawnImpact (projectile) s={shotIndex+1}/{numShots}");
							VfxManager.Instance.SpawnImpact(
								preset,
								attacker,
								target,
								targetPos,
								unitConfig,
								GetResolvedSkillIndex(),
								GetResolvedBuffDurationMs());
							bool isLast = (shotIndex == numShots - 1);
							// Post-impact behavior and animation only after the last shot
							if (isLast && moveEffect != null && moveEffect.postImpact != null)
							{
								HandlePostImpactForVfxEditor(moveEffect.postImpact, originalPosForPostImpact, idx);
							}
							var ua = attacker.GetComponent<UnitAnimator>();
							if (ua != null && isLast)
							{
								int remaining = Mathf.Max(100, Mathf.RoundToInt(ua.GetRemainingTimeSecondsForCurrentStateOneShot() * 1000f));
								int extra = 0;
								if (moveEffect != null && moveEffect.postImpact != null)
								{
									var b = moveEffect.postImpact.behavior;
									if (!string.IsNullOrEmpty(b) && b.ToLower() == "returnhome")
									{
										extra = PostImpactReturnDelayMs + EditorReturnDurationMs;
									}
								}
								EditorSchedule(() => { ua.CrossfadeToIdle(0.15f); StartEditorAnimTick(ua, EditorIdleCrossfadeMs); ua.ClearSkillIndex(); }, remaining + extra);
							}
						}, projectileTravelMs);
					}, baseDelay);
				}

				// Restore yaw after final shot completes
				EditorSchedule(() =>
				{
					var e = attacker.transform.eulerAngles;
					e.y = originalYaw;
					attacker.transform.eulerAngles = e;
				}, windMs + ((numShots - 1) * shotIntervalMs) + projectileTravelMs + EditorOneShotPaddingMs);

				// Invoke UnitSkillEvents end after last impact timing (mirror runtime end scheduling)
				if (invokeUnitEvents)
				{
					EditorSchedule(() =>
					{
						try { EditorInvokeSkillEnd(attacker, idx); } catch { }
					}, windMs + ((numShots - 1) * shotIntervalMs) + projectileTravelMs + EditorOneShotPaddingMs);
				}
			}
			else
			{
				// No projectile: spawn impacts repeatedly based on amount/interval
				for (int s = 0; s < numShots; s++)
				{
					int shotIndex = s;
					int baseDelay = windMs + (shotIndex * shotIntervalMs);
					Debug.Log($"[VFX Tester] Schedule impact shot {shotIndex+1}/{numShots} at +{baseDelay}ms (no projectile)");
					EditorSchedule(() =>
					{
						// Replay skill animation for additional shots in Edit Mode
						if (unitAnim != null && shotIndex > 0)
						{
							unitAnim.SetSkillIndex(idx);
							unitAnim.SetState(UnitState.Attacking);
							int animMs = Mathf.Max(1, Mathf.RoundToInt(unitAnim.GetCurrentStateLengthSeconds() * 1000f)) + EditorOneShotPaddingMs;
							StartEditorAnimTick(unitAnim, animMs);
						}
						Debug.Log($"[VFX Tester] SpawnImpact (no projectile) s={shotIndex+1}/{numShots}");
						VfxManager.Instance.SpawnImpact(
							preset,
							attacker,
							target,
							targetPos,
							unitConfig,
							GetResolvedSkillIndex(),
							GetResolvedBuffDurationMs());
						bool isLast = (shotIndex == numShots - 1);
						if (isLast && moveEffect != null && moveEffect.postImpact != null)
						{
							HandlePostImpactForVfxEditor(moveEffect.postImpact, originalPosForPostImpact, idx);
						}
						var ua = attacker.GetComponent<UnitAnimator>();
						if (ua != null && isLast)
						{
							int remaining = Mathf.Max(100, Mathf.RoundToInt(ua.GetRemainingTimeSecondsForCurrentStateOneShot() * 1000f));
							int extra = 0;
							if (moveEffect != null && moveEffect.postImpact != null)
							{
								var b = moveEffect.postImpact.behavior;
								if (!string.IsNullOrEmpty(b) && b.ToLower() == "returnhome")
								{
									extra = PostImpactReturnDelayMs + EditorReturnDurationMs;
								}
							}
							EditorSchedule(() => { ua.CrossfadeToIdle(0.15f); StartEditorAnimTick(ua, EditorIdleCrossfadeMs); ua.ClearSkillIndex(); }, remaining + extra);
						}
					}, baseDelay);
				}

				// Restore yaw shortly after final impact
				EditorSchedule(() =>
				{
					var e = attacker.transform.eulerAngles;
					e.y = originalYaw;
					attacker.transform.eulerAngles = e;
				}, windMs + ((numShots - 1) * shotIntervalMs) + EditorOneShotPaddingMs);

				// Invoke UnitSkillEvents end after last impact timing
				if (invokeUnitEvents)
				{
					EditorSchedule(() =>
					{
						try { EditorInvokeSkillEnd(attacker, idx); } catch { }
					}, windMs + ((numShots - 1) * shotIntervalMs) + EditorOneShotPaddingMs);
				}
			}
		}

		private void HandleMovementForVfx(UnitConfig.MoveEffect moveEffect, int windupMs, float attackOffsetWorldUnits)
		{
			if (moveEffect == null || attacker == null)
				return;

			// Make sure board has built its slot map when running in editor
			// Ensure boards in scene are discoverable/built
			#if UNITY_EDITOR
			Board.EditorEnsureInstance();
			var activeBoard = boardOverride != null ? boardOverride : ResolveBoardForUnit(attacker);
			if (activeBoard != null) activeBoard.EditorEnsureBuilt();
			#endif

			// Get current and target positions from the live board when possible
			var boardForAttacker = boardOverride != null ? boardOverride : ResolveBoardForUnit(attacker);
			if (boardForAttacker == null)
			{
				Debug.LogError("[VFX Movement] No board found in scene; cannot resolve movement.");
				return;
			}
			UnityEngine.Vector2Int currentPos = GetBoardCoord(attacker);
			UnityEngine.Vector2Int targetPos = (useTargetUnit && target != null)
				? GetBoardCoord(target)
				: (boardForAttacker != null ? GetNearestSlotCoordOnBoard(boardForAttacker, boardForAttacker.GetWorldPosition(targetCell)) ?? targetCell : targetCell);

			// Debug logging
			Debug.Log($"[VFX Movement] Attacker at {currentPos}, Target at {targetPos}, Distance: {Mathf.Max(Mathf.Abs(targetPos.x - currentPos.x), Mathf.Abs(targetPos.y - currentPos.y))}");
			var dbgBoard = boardForAttacker;
			Debug.Log($"[VFX Movement] Board slots cached: {(dbgBoard != null ? GetBoardSlotsCount(dbgBoard) : 0)} (board: {dbgBoard?.name ?? "null"})");
			if (dbgBoard != null)
			{
				// Extra debug: print attacker/target world pos and nearest slot
				Vector3 aw = attacker.transform.position;
				var an = GetNearestSlotCoordOnBoard(dbgBoard, aw);
				Vector3? anW = an.HasValue ? dbgBoard.GetSlotWorldPosition(an.Value) : (Vector3?)null;
				Debug.Log($"[VFX Movement] Attacker world {aw}, nearest slot {(an.HasValue ? an.Value.ToString() : "none")} @ {(anW.HasValue ? anW.Value.ToString() : "-")}");
				if (useTargetUnit && target != null)
				{
					Vector3 tw = target.transform.position;
					var tn = GetNearestSlotCoordOnBoard(dbgBoard, tw);
					Vector3? tnW = tn.HasValue ? dbgBoard.GetSlotWorldPosition(tn.Value) : (Vector3?)null;
					Debug.Log($"[VFX Movement] Target world {tw}, nearest slot {(tn.HasValue ? tn.Value.ToString() : "none")} @ {(tnW.HasValue ? tnW.Value.ToString() : "-")}");
				}
			}

			// Validate that the target position is actually reachable according to the pattern
			if (!UnitConfig.IsValidMovementTarget(currentPos, targetPos, moveEffect, attacker.OwnerId.ToString()))
			{
				Debug.LogWarning($"[VFX Movement] Movement target {targetPos} is not valid for pattern. Skipping movement.");
				Debug.Log($"[VFX Movement] Pattern info: Direction={moveEffect.patterns[0]?.dir}, Range=[{moveEffect.patterns[0]?.rangeMin},{moveEffect.patterns[0]?.rangeMax}], OwnerId={attacker.OwnerId}");
				return;
			}

			// Calculate movement path based on patterns
			UnityEngine.Vector2Int movementTarget = UnitConfig.CalculateMovementPath(currentPos, targetPos, moveEffect, attacker.OwnerId.ToString());
			var originalMovementTarget = movementTarget;
			Debug.Log($"[VFX Movement] Calculated movement target (pre-adjust): {movementTarget} (current: {currentPos})");

			// Mirror JS stopOnHit: if moving toward an occupied target, land just before it
			bool stopOnHit = false;
			if (moveEffect.patterns != null && moveEffect.patterns.Count > 0 && moveEffect.patterns[0] != null)
			{
				stopOnHit = moveEffect.patterns[0].stopOnHit;
			}
			if (stopOnHit && boardForAttacker != null)
			{
				var dir = new UnityEngine.Vector2Int(Mathf.Clamp(targetPos.x - currentPos.x, -1, 1), Mathf.Clamp(targetPos.y - currentPos.y, -1, 1));
				if (dir != UnityEngine.Vector2Int.zero)
				{
					// Editor preview: always land exactly one cell before the target along approach vector
					var candidate = new UnityEngine.Vector2Int(targetPos.x - dir.x, targetPos.y - dir.y);
					if (candidate.x >= 0 && candidate.x < Board.Size && candidate.y >= 0 && candidate.y < Board.Size)
					{
						movementTarget = candidate;
					}
				}
			}
			// Prevent moving onto an occupied target cell in editor (GameManager may be null so runtime check won't catch it)
			if (useTargetUnit && target != null)
			{
				var targetCoord = GetBoardCoord(target);
				if (movementTarget == targetCoord)
				{
					var dir = new UnityEngine.Vector2Int(Mathf.Clamp(targetCoord.x - currentPos.x, -1, 1), Mathf.Clamp(targetCoord.y - currentPos.y, -1, 1));
					var before = new UnityEngine.Vector2Int(targetCoord.x - dir.x, targetCoord.y - dir.y);
					if (before.x >= 0 && before.x < Board.Size && before.y >= 0 && before.y < Board.Size && !boardForAttacker.IsOccupied(before))
					{
						Debug.Log($"[VFX Movement] Adjusted to avoid occupied target: {movementTarget} -> {before}");
						movementTarget = before;
					}
				}
			}
			if (movementTarget != originalMovementTarget)
			{
				Debug.Log($"[VFX Movement] Adjusted for stopOnHit: {originalMovementTarget} -> {movementTarget}");
			}
			Debug.Log($"[VFX Movement] Final movement target: {movementTarget} (current: {currentPos})");

			// If movement target is different from current position, move the unit
			if (movementTarget != currentPos)
			{
				var finalTarget = movementTarget; // freeze value for logs and execution
				Debug.Log($"[VFX Movement] Movement approved! Moving from {currentPos} to {finalTarget}");

				// Move immediately and complete exactly at windup end
				try
				{
					PerformEditModeMovement(boardForAttacker, finalTarget, moveEffect, currentPos, Mathf.Max(0, windupMs), Mathf.Max(0f, attackOffsetWorldUnits));
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"Error during VFX movement: {ex.Message}");
				}
			}
			else
			{
				Debug.LogWarning($"[VFX Movement] Movement target same as current position. No movement needed.");
			}
		}

		private void PerformEditModeMovement(Board board, UnityEngine.Vector2Int movementTarget, UnitConfig.MoveEffect moveEffect, UnityEngine.Vector2Int originalPos, int movementDurationMs, float attackOffsetWorldUnits)
		{
			if (attacker == null)
			{
				Debug.LogError("[VFX Movement] Attacker is null!");
				return;
			}

			if (board == null)
			{
				Debug.LogError("[VFX Movement] Could not resolve a Board for attacker. Ensure a Board exists in the scene.");
				return;
			}

			// Cancel any previous unfinished move tween
			CancelEditorMoveTween();

			// Prepare tween data
			Vector3 targetWorldCenter = board.GetSlotWorldPosition(movementTarget);
			Vector3 startWorldCenter = board.GetSlotWorldPosition(originalPos);
			Vector3 targetWorldPos = targetWorldCenter;
			Debug.Log($"[VFX Movement] World start {startWorldCenter} -> world target {targetWorldCenter} (offset {attackOffsetWorldUnits}) on board '{board.name}'");
			if (attackOffsetWorldUnits > 0f)
			{
				Vector3 dir = (targetWorldCenter - startWorldCenter).normalized;
				if (dir.sqrMagnitude > 0.0001f)
				{
					targetWorldPos = targetWorldCenter - (dir * attackOffsetWorldUnits);
				}
			}
			editorMovingUnit = attacker;
			editorMovingBoard = board;
			editorMoveStartPos = attacker.transform.position;
			editorMoveTargetPos = targetWorldPos;
			editorMoveOriginalCoord = originalPos;
			editorMoveTargetCoord = movementTarget;
			editorMoveStartAt = EditorApplication.timeSinceStartup;
			editorMoveEndAt = editorMoveStartAt + (Mathf.Max(0, movementDurationMs) / 1000.0);
			// Start ticking the editor move tween
			HookEditorMoveTick();

			// Post-impact behavior is triggered after impact spawn in PlayVfx()
			// As a safety, if no post-impact return is pending, schedule a small idle crossfade
			var ua = attacker.GetComponent<UnitAnimator>();
			if (ua != null)
			{
				EditorSchedule(() => { ua.CrossfadeToIdle(0.15f); ua.ClearSkillIndex(); }, Mathf.Max(50, movementDurationMs + 50));
			}
		}

		private float EstimateTileWorldSize(Board board, UnityEngine.Vector2Int coord)
		{
			if (board == null) return 1f;
			var right = new UnityEngine.Vector2Int(coord.x + 1, coord.y);
			var up = new UnityEngine.Vector2Int(coord.x, coord.y + 1);
			Vector3 c = board.GetSlotWorldPosition(coord);
			if (right.x < Board.Size)
			{
				Vector3 r = board.GetSlotWorldPosition(right);
				return Mathf.Max(0.001f, Vector3.Distance(c, r));
			}
			if (up.y < Board.Size)
			{
				Vector3 u = board.GetSlotWorldPosition(up);
				return Mathf.Max(0.001f, Vector3.Distance(c, u));
			}
			return 1f;
		}

		private UnityEngine.Vector2Int GetBoardCoord(Unit unit)
		{
			if (unit == null) return UnityEngine.Vector2Int.zero;
			var board = ResolveBoardForUnit(unit);
			if (board != null)
			{
				if (board.TryGetCoord(unit, out var coord))
				{
					return coord;
				}
				// Fallback A: find closest slot by distance to transform on this board
				var nearest = GetNearestSlotCoordOnBoard(board, unit.transform.position);
				if (nearest.HasValue)
				{
					Debug.Log($"[VFX Movement] Using nearest slot for {unit.name}: {nearest.Value} (board: {board.name})");
					return nearest.Value;
				}
				// Fallback B: derive from transform via board origin math
				var fromWorld = board.GetCoordinateFromWorld(unit.transform.position);
				fromWorld.x = Mathf.Clamp(fromWorld.x, 0, Board.Size - 1);
				fromWorld.y = Mathf.Clamp(fromWorld.y, 0, Board.Size - 1);
				Debug.Log($"[VFX Movement] Using coordinate-from-world for {unit.name}: {fromWorld} (board: {board.name})");
				return fromWorld;
			}
			// Last resort: serialized position on the Unit (may be stale in edit mode)
			return new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y);
		}

		private UnityEngine.Vector2Int? GetNearestSlotCoordOnBoard(Board board, Vector3 worldPos)
		{
			if (board == null) return null;
			float bestSqr = float.PositiveInfinity;
			UnityEngine.Vector2Int best = default;
			bool found = false;
			foreach (var kv in board.GetAllSlots())
			{
				var slot = kv.Value;
				if (slot == null) continue;
				float d2 = (slot.position - worldPos).sqrMagnitude;
				if (d2 < bestSqr)
				{
					bestSqr = d2;
					best = kv.Key;
					found = true;
				}
			}
			return found ? best : (UnityEngine.Vector2Int?)null;
		}

		private int GetBoardSlotsCount(Board board)
		{
			int count = 0;
			if (board == null) return 0;
			foreach (var _ in board.GetAllSlots()) count++;
			return count;
		}

		private Board ResolveBoardForUnit(Unit unit)
		{
			#if UNITY_EDITOR
			var allBoards = UnityEngine.Object.FindObjectsByType<Board>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
			#else
			var allBoards = new Board[] { Board.Instance };
			#endif
			Board bestBoard = null;
			float bestDist = float.PositiveInfinity;
			if (allBoards == null || allBoards.Length == 0) return null;
			for (int i = 0; i < allBoards.Length; i++)
			{
				var b = allBoards[i];
				if (b == null) continue;
				#if UNITY_EDITOR
				b.EditorEnsureBuilt();
				#endif
				// heuristic: nearest slot center to unit transform
				var nearest = GetNearestSlotCoordOnBoard(b, unit != null ? unit.transform.position : Vector3.zero);
				if (nearest.HasValue)
				{
					var world = b.GetSlotWorldPosition(nearest.Value);
					float d = (world - (unit != null ? unit.transform.position : world)).sqrMagnitude;
					if (d < bestDist)
					{
						bestDist = d;
						bestBoard = b;
					}
				}
			}
			return bestBoard ?? Board.Instance;
		}

		private void HandlePostImpactForVfxEditor(UnitConfig.PostImpact postImpact, UnityEngine.Vector2Int originalPos, int skillIndex)
		{
			if (postImpact == null || attacker == null)
				return;

			switch (postImpact.behavior?.ToLower())
			{
				case "returnhome":
					// Schedule return to original position after a brief delay
					EditorSchedule(() =>
					{
						try
						{
							// Resolve board and positions
							var board = ResolveBoardForUnit(attacker);
							if (board != null)
							{
								Vector3 homeWorldPos = board.GetSlotWorldPosition(originalPos);
								// Play return-home animation if present
								var ua = attacker.GetComponent<UnitAnimator>();
								if (ua != null)
								{
									ua.SetSkillIndex(skillIndex);
									ua.PlayReturnHomeState(skillIndex);
									StartEditorAnimTick(ua, EditorReturnDurationMs + 50);
								}
								// Tween back to home over a short duration
								editorMovingUnit = attacker;
								editorMovingBoard = board;
								editorMoveStartPos = attacker.transform.position;
								editorMoveTargetPos = homeWorldPos;
								editorMoveOriginalCoord = editorMoveTargetCoord; // coming back from last move target
								editorMoveTargetCoord = originalPos;
								editorMoveStartAt = EditorApplication.timeSinceStartup;
								editorMoveEndAt = editorMoveStartAt + (EditorReturnDurationMs / 1000.0);
								HookEditorMoveTick();
								// After return tween ends, force a final Idle crossfade/tick to ensure pose updates in edit mode
								EditorSchedule(() =>
								{
									var ua2 = attacker.GetComponent<UnitAnimator>();
									if (ua2 != null)
									{
										ua2.CrossfadeToIdle(0.15f);
										StartEditorAnimTick(ua2, EditorIdleCrossfadeMs);
									}
								}, EditorReturnDurationMs + 10);
							}
						}
						catch (System.Exception ex)
						{
							Debug.LogError($"Error during return home movement: {ex.Message}");
						}
					}, PostImpactReturnDelayMs);
					break;

				case "landneartarget":
					EditorSchedule(() =>
					{
						try
						{
							var board = ResolveBoardForUnit(attacker);
							if (board == null) return;
							var targetCoord = useTargetUnit && target != null ? GetBoardCoord(target) : (Vector2Int?)GetNearestSlotCoordOnBoard(board, VfxManager.Instance != null && attacker != null && VfxManager.Instance.TryGetRememberedTargetPosition(attacker.UnitID, out var w) ? w : attacker.transform.position);
							if (!targetCoord.HasValue) return;
							int radius = Mathf.Max(1, postImpact.radius);
							bool pickRandom = postImpact.random;
							// Prefer the cell directly before the target along the attacker->target vector when empty
							var from = GetBoardCoord(attacker);
							var dir = new Vector2Int(Mathf.Clamp(targetCoord.Value.x - from.x, -1, 1), Mathf.Clamp(targetCoord.Value.y - from.y, -1, 1));
							if (dir != Vector2Int.zero)
							{
								var immediate = new Vector2Int(targetCoord.Value.x - dir.x, targetCoord.Value.y - dir.y);
								if (immediate.x >= 0 && immediate.x < Board.Size && immediate.y >= 0 && immediate.y < Board.Size && !board.IsOccupied(immediate))
								{
									editorMovingUnit = attacker;
									editorMovingBoard = board;
									editorMoveStartPos = attacker.transform.position;
									editorMoveTargetPos = board.GetSlotWorldPosition(immediate);
									editorMoveOriginalCoord = editorMoveTargetCoord;
									editorMoveTargetCoord = immediate;
									editorMoveStartAt = EditorApplication.timeSinceStartup;
									editorMoveEndAt = editorMoveStartAt + (EditorReturnDurationMs / 1000.0);
									HookEditorMoveTick();
									return;
								}
							}
							// Fallback to ring search around target
							var chosen = EditorFindLandNearTargetCell(board, targetCoord.Value, radius, pickRandom);
							if (chosen.HasValue)
							{
								editorMovingUnit = attacker;
								editorMovingBoard = board;
								editorMoveStartPos = attacker.transform.position;
								editorMoveTargetPos = board.GetSlotWorldPosition(chosen.Value);
								editorMoveOriginalCoord = editorMoveTargetCoord;
								editorMoveTargetCoord = chosen.Value;
								editorMoveStartAt = EditorApplication.timeSinceStartup;
								editorMoveEndAt = editorMoveStartAt + (EditorReturnDurationMs / 1000.0);
								HookEditorMoveTick();
							}
						}
						catch { }
					}, PostImpactReturnDelayMs);
					break;

				default:
					break;
			}
		}

		private Vector3 ResolveEditorProjectileTargetWorldPos(SkillVfxPreset preset)
		{
			// Fallback to target cell or attacker pos if no target
			Vector3 fallback = Board.Instance != null ? Board.Instance.GetSlotWorldPosition(targetCell) : (attacker != null ? attacker.transform.position : Vector3.zero);
			if (target == null) return fallback;
			var ta = target.GetComponent<UnitVfxAnchors>();
			switch (preset != null ? preset.ProjectileTarget : SkillVfxPreset.TargetAnchor.TargetImpactAnchor)
			{
				case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
				{
					Transform anch = ta != null ? ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.TargetImpactAnchor, preset.CustomTargetAnchorName) : null;
					return anch != null ? anch.position : target.transform.position;
				}
				case SkillVfxPreset.TargetAnchor.TargetRoot:
					return target.transform.position;
				case SkillVfxPreset.TargetAnchor.Custom:
				{
					Transform anch = ta != null ? ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.Custom, preset.CustomTargetAnchorName) : null;
					return anch != null ? anch.position : target.transform.position;
				}
				case SkillVfxPreset.TargetAnchor.WorldAtTargetCell:
				default:
					return fallback;
			}
		}

		private UnityEngine.Vector2Int? EditorFindLandNearTargetCell(Board board, UnityEngine.Vector2Int target, int radius, bool random)
		{
			if (board == null) return null;
			radius = Mathf.Max(1, radius);
			System.Collections.Generic.List<UnityEngine.Vector2Int> candidates = new System.Collections.Generic.List<UnityEngine.Vector2Int>(radius * radius * 8);
			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (dx == 0 && dy == 0) continue;
					int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
					if (cheb < 1 || cheb > radius) continue;
					int x = target.x + dx;
					int y = target.y + dy;
					if (x < 0 || x >= Board.Size || y < 0 || y >= Board.Size) continue;
					var cell = new UnityEngine.Vector2Int(x, y);
					if (!board.IsOccupied(cell)) candidates.Add(cell);
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
				var from = GetBoardCoord(attacker);
				var dir = new Vector2Int(Mathf.Clamp(target.x - from.x, -1, 1), Mathf.Clamp(target.y - from.y, -1, 1));
				if (dir != Vector2Int.zero)
				{
					var immediate = new Vector2Int(target.x - dir.x, target.y - dir.y);
					for (int i = 0; i < candidates.Count; i++) { if (candidates[i] == immediate) return immediate; }
				}
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

		private void EditorSyncUnitToNearestBoardSlot(Unit u)
		{
			if (u == null) return;
			var board = boardOverride != null ? boardOverride : ResolveBoardForUnit(u);
			if (board == null) return;
			#if UNITY_EDITOR
			board.EditorEnsureBuilt();
			#endif
			UnityEngine.Vector2Int nearest;
			var nn = GetNearestSlotCoordOnBoard(board, u.transform.position);
			if (!nn.HasValue) return;
			nearest = nn.Value;
			UnityEngine.Vector2Int existing;
			bool had = false;
			try { had = board.TryGetCoord(u, out existing); } catch { had = false; existing = nearest; }
			if (had && existing == nearest) return;
			try { if (had) board.ClearOccupied(existing, u); } catch { }
			try { board.SetOccupied(nearest, u); } catch { }
			var currentPositionField = typeof(Unit).GetField("currentPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (currentPositionField != null)
			{
				currentPositionField.SetValue(u, new UnityEngine.Vector2Int(nearest.x, nearest.y));
			}
		}
	}
}

