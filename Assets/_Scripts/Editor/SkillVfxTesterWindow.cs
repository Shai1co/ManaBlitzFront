using System;
using UnityEditor;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
	public partial class SkillVfxTesterWindow : EditorWindow
	{
		private const int DefaultDelayMs = 600;
		private const int DefaultServerTick = 1000;
		private const string MenuPath = "Tools/ManaGambit/Skill VFX Tester";
		private const int EditorAnimPaddingMs = 1000;
		private const int EditorOneShotPaddingMs = 50;

		private Unit attacker;
		private Unit target;
		private bool useTargetUnit = true;
		private Vector2Int targetCell;
		private UnitConfig unitConfig;
		private int skillIndex;
		private bool selectByActionName;
		private string actionName;
		private string[] availableActionNames = System.Array.Empty<string>();
		private int availableActionsCount;
		private int selectedActionDropIdx = -1;
		private int serverTick = DefaultServerTick;
		private int delayMs = DefaultDelayMs;
		private bool overrideWindupMs;
		private int windupMsOverride = 400;
		// Editor behavior: optionally fire UnitSkillEvents to let user-bound events run (may duplicate tester-driven actions)
		private bool invokeUnitEvents = false;

		private double scheduledImpactAt = -1d;
		private SkillVfxPreset cachedPreset;

		// Editor animation ticking
		private UnitAnimator animToTick;
		private double animTickEndAt = -1d;
		private double lastAnimTickTime = -1d;
		private bool animTickHooked;
		// Optional board override for editor testing (use correct board explicitly)
		private Board boardOverride;

		// Editor unit movement ticking
		private const int EditorMoveDurationMs = 300; // short tween for visual feedback in editor
		private Unit editorMovingUnit;
		private Board editorMovingBoard;
		private Vector3 editorMoveStartPos;
		private Vector3 editorMoveTargetPos;
		private Vector2Int editorMoveOriginalCoord;
		private Vector2Int editorMoveTargetCoord;
		private double editorMoveStartAt = -1d;
		private double editorMoveEndAt = -1d;

		[MenuItem(MenuPath)]
		public static void Open()
		{
			var w = GetWindow<SkillVfxTesterWindow>();
			w.titleContent = new GUIContent("Skill VFX Tester");
			w.minSize = new Vector2(360, 320);
		}

		private void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			if (unitConfig == null && NetworkManager.Instance != null)
			{
				unitConfig = NetworkManager.Instance.UnitConfigAsset;
			}
		}

		private void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
		}

		private void OnGUI()
		{
			if (VfxManager.Instance == null)
			{
				VfxManager.EditorEnsureInstance();
			}

			EditorGUILayout.LabelField("Attacker & Target", EditorStyles.boldLabel);
			attacker = (Unit)EditorGUILayout.ObjectField("Attacker Unit", attacker, typeof(Unit), true);
			target = (Unit)EditorGUILayout.ObjectField("Target Unit (opt)", target, typeof(Unit), true);
			useTargetUnit = EditorGUILayout.Toggle("Use Target Unit", useTargetUnit);
			targetCell = EditorGUILayout.Vector2IntField("Target Cell (x,y)", targetCell);

			EditorGUILayout.Space();
			invokeUnitEvents = EditorGUILayout.Toggle("Invoke Unit Events (Edit Mode)", invokeUnitEvents);
			boardOverride = (Board)EditorGUILayout.ObjectField("Board Override (opt)", boardOverride, typeof(Board), true);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
			unitConfig = (UnitConfig)EditorGUILayout.ObjectField("UnitConfig", unitConfig, typeof(UnitConfig), false);

			// Build and show action dropdown for current attacker/piece
			RebuildAvailableActions();
			using (new EditorGUI.DisabledScope(attacker == null || unitConfig == null || availableActionsCount == 0))
			{
				int newIdx = EditorGUILayout.Popup("Action (from UnitConfig)", Mathf.Max(0, selectedActionDropIdx), availableActionNames);
				if (newIdx != selectedActionDropIdx)
				{
					selectedActionDropIdx = newIdx;
					skillIndex = selectedActionDropIdx;
					var resolvedName = GetResolvedActionName();
					if (!string.IsNullOrEmpty(resolvedName)) actionName = resolvedName;
				}
			}
			selectByActionName = EditorGUILayout.Toggle("Select by Action Name", selectByActionName);
			if (selectByActionName)
			{
				actionName = EditorGUILayout.TextField("Action Name", actionName);
			}
			else
			{
				skillIndex = EditorGUILayout.IntField("Skill Index", skillIndex);
			}
			serverTick = EditorGUILayout.IntField("Server Tick", serverTick);
			delayMs = EditorGUILayout.IntField("Delay to Impact (ms)", delayMs);
			overrideWindupMs = EditorGUILayout.Toggle("Override Windup (ms)", overrideWindupMs);
			if (overrideWindupMs)
			{
				windupMsOverride = EditorGUILayout.IntField("Windup (ms)", windupMsOverride);
			}

			EditorGUILayout.Space();
			cachedPreset = ResolvePreset();
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("Resolved Preset", cachedPreset, typeof(SkillVfxPreset), false);
				EditorGUILayout.TextField("Resolved Action Name", GetResolvedActionName() ?? string.Empty);
				EditorGUILayout.IntField("Resolved Skill Index", GetResolvedSkillIndex());
				int presetWind = GetPresetWindupMs(cachedPreset);
				EditorGUILayout.IntField("Preset Windup (ms)", presetWind);
				EditorGUILayout.Toggle("Preset Has Buff Prefab", cachedPreset != null && cachedPreset.BuffPrefab != null);
				EditorGUILayout.IntField("Resolved Buff Duration (ms)", GetResolvedBuffDurationMs());
			}

			EditorGUILayout.Space();
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Reload Data"))
			{
				ReloadAllData();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
			if (GUILayout.Button("Play VFX"))
			{
				// call into partial's PlayVfx if available
				try { var m = GetType().GetMethod("PlayVfx", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); if (m != null) m.Invoke(this, null); } catch { }
			}

			EditorGUILayout.Space();
			if (scheduledImpactAt > 0)
			{
				EditorGUILayout.HelpBox($"Impact scheduled in {(scheduledImpactAt - EditorApplication.timeSinceStartup):0.00}s", MessageType.Info);
				if (GUILayout.Button("Cancel Scheduled Impact"))
				{
					scheduledImpactAt = -1d;
				}
			}
		}

		private SkillVfxPreset ResolvePreset()
		{
			if (VfxManager.Instance == null || attacker == null || unitConfig == null) return null;
			int idx = GetResolvedSkillIndex();
			if (idx >= 0) return VfxManager.Instance.GetPresetByPieceAndIndex(attacker.PieceId, idx, unitConfig);
			var name = GetResolvedActionName();
			if (!string.IsNullOrEmpty(name)) return VfxManager.Instance.GetPresetByActionName(name);
			return null;
		}

		private int GetResolvedBuffDurationMs()
		{
			if (unitConfig == null || attacker == null) return 0;
			var data = unitConfig.GetData(attacker.PieceId);
			if (data == null || data.actions == null) return 0;
			int idx = GetResolvedSkillIndex();
			if (idx < 0 || idx >= data.actions.Length) return 0;
			var a = data.actions[idx];
			return Mathf.Max(0, a?.buff?.duration ?? 0);
		}

		private int GetResolvedSkillIndex()
		{
			if (!selectByActionName)
			{
				return Mathf.Max(0, skillIndex);
			}
			if (unitConfig != null && attacker != null && !string.IsNullOrEmpty(actionName))
			{
				int idx = unitConfig.GetActionIndexByName(attacker.PieceId, actionName);
				return idx >= 0 ? idx : Mathf.Max(0, skillIndex);
			}
			return Mathf.Max(0, skillIndex);
		}

		private string GetResolvedActionName()
		{
			if (selectByActionName && !string.IsNullOrEmpty(actionName)) return actionName;
			if (unitConfig != null && attacker != null)
			{
				return unitConfig.GetActionName(attacker.PieceId, Mathf.Max(0, skillIndex));
			}
			return null;
		}

		private int GetPresetWindupMs(SkillVfxPreset preset)
		{
			if (preset == null) return 0;
			switch (preset.WindupPolicy)
			{
				case SkillVfxPreset.WindupDurationPolicy.FixedMs:
					return Mathf.Max(0, preset.FixedWindupMs);
				case SkillVfxPreset.WindupDurationPolicy.FromTicks:
					// FromTicks uses server ticks; in the tester, reflect UnitConfig as reference
					return unitConfig != null && attacker != null ? unitConfig.GetWindUpMs(attacker.PieceId, GetResolvedSkillIndex()) : 0;
				default:
					return 0;
			}
		}

		private void RebuildAvailableActions()
		{
			if (attacker == null || unitConfig == null)
			{
				availableActionNames = System.Array.Empty<string>();
				availableActionsCount = 0;
				selectedActionDropIdx = -1;
				return;
			}
			var data = unitConfig.GetData(attacker.PieceId);
			if (data == null || data.actions == null || data.actions.Length == 0)
			{
				availableActionNames = System.Array.Empty<string>();
				availableActionsCount = 0;
				selectedActionDropIdx = -1;
				return;
			}
			availableActionsCount = Mathf.Max(0, data.actions.Length);
			if (availableActionNames == null || availableActionNames.Length != availableActionsCount)
			{
				availableActionNames = new string[availableActionsCount];
			}
			for (int i = 0; i < availableActionsCount; i++)
			{
				var a = data.actions[i];
				availableActionNames[i] = a != null && !string.IsNullOrEmpty(a.name) ? a.name : $"Action {i}";
			}
			if (selectedActionDropIdx < 0 || selectedActionDropIdx >= availableActionsCount)
			{
				selectedActionDropIdx = Mathf.Clamp(skillIndex, 0, Mathf.Max(0, availableActionsCount - 1));
			}
		}

		private UseSkillData BuildUseSkillData()
		{
			var use = new UseSkillData
			{
				unitId = attacker != null ? attacker.UnitID : string.Empty,
				skillId = Mathf.Max(0, GetResolvedSkillIndex()),
				origin = new Pos { x = attacker != null ? attacker.CurrentPosition.x : 0, y = attacker != null ? attacker.CurrentPosition.y : 0 },
				target = new SkillTarget()
			};
			if (useTargetUnit && target != null)
			{
				use.target.cell = new Pos { x = target.CurrentPosition.x, y = target.CurrentPosition.y };
			}
			else
			{
				use.target.cell = new Pos { x = targetCell.x, y = targetCell.y };
			}

			int resolvedIndex = GetResolvedSkillIndex();
			int windupMs = overrideWindupMs ? Mathf.Max(0, windupMsOverride) : (unitConfig != null ? Mathf.Max(0, unitConfig.GetWindUpMs(attacker.PieceId, Mathf.Max(0, resolvedIndex))) : 0);
			int windupTicks = Mathf.RoundToInt(windupMs / NetworkManager.DefaultTickRateMs);
			int delayTicks = Mathf.RoundToInt(Mathf.Max(0, delayMs) / NetworkManager.DefaultTickRateMs);

			use.startTick = serverTick;
			use.endWindupTick = serverTick + windupTicks;
			use.hitTick = serverTick + delayTicks;
			return use;
		}

		private void SimulateUseSkill()
		{
		}


		private void EditorSchedule(Action action, int delayMs)
		{
			if (action == null || delayMs < 0) return;
			double runAt = EditorApplication.timeSinceStartup + delayMs / 1000.0;
			void Tick()
			{
				if (EditorApplication.timeSinceStartup >= runAt)
				{
					EditorApplication.update -= Tick;
					try { action(); } catch { }
				}
			}
			EditorApplication.update += Tick;
		}

		private void SimulateAnimWindupThenImpact()
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

			var unitAnim = attacker.GetComponent<UnitAnimator>();
			int windMs = overrideWindupMs ? Mathf.Max(0, windupMsOverride) : unitConfig.GetWindUpMs(attacker.PieceId, idx);
			if (!Application.isPlaying && unitAnim != null)
			{
				unitAnim.SetSkillIndex(idx);
				// Play the skill animation during windup
				unitAnim.SetState(UnitState.Attacking);
				StartEditorAnimTick(unitAnim, windMs + 1000);
			}

			var anchors = attacker.GetComponent<UnitVfxAnchors>();
			var windAnchor = anchors != null ? anchors.FindSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName) : attacker.transform;
			if (preset.WindupPrefab != null && windMs > 0)
			{
				VfxManager.Instance.PlayAtEditor(windAnchor, preset.WindupPrefab, windMs);
			}

			Vector3 targetPos = target != null ? target.transform.position : (Board.Instance != null ? Board.Instance.GetSlotWorldPosition(targetCell) : attacker.transform.position);
			VfxManager.Instance.RememberTargetPosition(attacker.UnitID, targetPos);
			EditorSchedule(() =>
			{
				// Spawn impact at windup end, but do not interrupt the skill animation
				// Pass UnitConfig and resolved action index so editor can resolve buff durations correctly
				// Force the buff duration we resolved from UnitConfig to avoid any mismatch
				VfxManager.Instance.SpawnImpact(
					preset,
					attacker,
					target,
					targetPos,
					unitConfig,
					GetResolvedSkillIndex(),
					GetResolvedBuffDurationMs());
				var ua = attacker.GetComponent<UnitAnimator>();
				if (ua != null)
				{
					int remainingMs = Mathf.Max(100, Mathf.RoundToInt(ua.GetRemainingTimeSecondsForCurrentStateOneShot() * 1000f));
					EditorSchedule(() =>
					{
						ua.SetState(UnitState.Idle);
						ua.ClearSkillIndex();
					}, remainingMs);
				}
			}, windMs);
		}

		private void SpawnImpactNow()
		{
			if (VfxManager.Instance == null)
			{
				ShowNotification(new GUIContent("Missing VfxManager in scene"));
				return;
			}
			if (attacker == null)
			{
				ShowNotification(new GUIContent("Assign an attacker"));
				return;
			}
			if (unitConfig == null)
			{
				ShowNotification(new GUIContent("Assign UnitConfig"));
				return;
			}
			var preset = ResolvePreset();
			if (preset == null || preset.ImpactPrefab == null)
			{
				ShowNotification(new GUIContent("No impact preset for this skill"));
				return;
			}
			var fallbackPos = target != null ? target.transform.position : (Board.Instance != null ? Board.Instance.GetSlotWorldPosition(targetCell) : attacker.transform.position);
			VfxManager.Instance.SpawnImpact(
				preset,
				attacker,
				target,
				fallbackPos,
				unitConfig,
				GetResolvedSkillIndex(),
				GetResolvedBuffDurationMs());
		}

		private void ScheduleImpact(int inMs)
		{
			scheduledImpactAt = EditorApplication.timeSinceStartup + Mathf.Max(0, inMs) / 1000.0;
		}

		private void OnEditorUpdate()
		{
			if (scheduledImpactAt > 0 && EditorApplication.timeSinceStartup >= scheduledImpactAt)
			{
				scheduledImpactAt = -1d;
				SpawnImpactNow();
			}
			TickEditorAnimator();
			TickEditorMove();
		}

		private void StartEditorAnimTick(UnitAnimator animator, int runForMs)
		{
			if (animator == null || runForMs <= 0) return;
			animToTick = animator;
			lastAnimTickTime = EditorApplication.timeSinceStartup;
			animTickEndAt = lastAnimTickTime + (runForMs / 1000.0);
			HookAnimTick();
		}

		private void HookAnimTick()
		{
			if (animTickHooked) return;
			EditorApplication.update += TickEditorAnimator;
			animTickHooked = true;
		}

		private void HookEditorMoveTick()
		{
			if (!animTickHooked)
			{
				EditorApplication.update += TickEditorAnimator;
				animTickHooked = true;
			}
		}

		private void TickEditorAnimator()
		{
			if (animToTick == null) return;
			double now = EditorApplication.timeSinceStartup;
			float dt = (float)System.Math.Max(0.0, now - lastAnimTickTime);
			lastAnimTickTime = now;
			if (dt > 0f)
			{
				try { animToTick.EditorTick(dt); } catch { }
			}
			if (now >= animTickEndAt)
			{
				EditorApplication.update -= TickEditorAnimator;
				animTickHooked = false;
				animToTick = null;
				SceneView.RepaintAll();
			}
		}

		private void TickEditorMove()
		{
			if (editorMovingUnit == null || editorMovingBoard == null || editorMoveEndAt <= 0d) return;
			double now = EditorApplication.timeSinceStartup;
			float t = 0f;
			if (editorMoveEndAt > editorMoveStartAt)
			{
				t = Mathf.Clamp01((float)((now - editorMoveStartAt) / (editorMoveEndAt - editorMoveStartAt)));
			}
			editorMovingUnit.transform.position = Vector3.Lerp(editorMoveStartPos, editorMoveTargetPos, t);
			SceneView.RepaintAll();
			if (now >= editorMoveEndAt)
			{
				FinalizeEditorMoveTween();
			}
		}

		// Editor-only safe invocations of UnitSkillEvents to avoid SendMessage asserts
		private void EditorInvokeSkillStart(Unit unit, int skillIndex)
		{
			if (unit == null) return;
			var ev = unit.GetComponent<UnitSkillEvents>();
			if (ev == null) return;
			try { ev.InvokeForSkillIndex(Mathf.Max(0, skillIndex)); } catch { }
		}

		private void EditorInvokeSkillEnd(Unit unit, int skillIndex)
		{
			if (unit == null) return;
			var ev = unit.GetComponent<UnitSkillEvents>();
			if (ev == null) return;
			try { ev.InvokeEndForSkillIndex(Mathf.Max(0, skillIndex)); } catch { }
		}

		private void FinalizeEditorMoveTween()
		{
			if (editorMovingUnit == null || editorMovingBoard == null) { editorMoveEndAt = -1d; return; }
			// finalize position/coords/occupancy
			var currentPositionField = typeof(Unit).GetField("currentPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (currentPositionField != null)
			{
				currentPositionField.SetValue(editorMovingUnit, new Vector2Int(editorMoveTargetCoord.x, editorMoveTargetCoord.y));
			}
			try
			{
				editorMovingBoard.ClearOccupied(editorMoveOriginalCoord, editorMovingUnit);
				editorMovingBoard.SetOccupied(editorMoveTargetCoord, editorMovingUnit);
			}
			catch { }
			// clear state
			editorMovingUnit = null;
			editorMovingBoard = null;
			editorMoveEndAt = -1d;
		}

		private void CancelEditorMoveTween()
		{
			// If a previous move is still pending/ticking, finalize it instantly so a new move can start
			if (editorMoveEndAt > 0d && editorMovingUnit != null)
			{
				editorMoveStartAt = 0d;
				editorMoveEndAt = 0d;
				FinalizeEditorMoveTween();
			}
		}

		private void ReloadAllData()
		{
			// Reload VFX presets data
			ReloadVfxPresetsData();

			// Clear cached data in VfxManager
			ClearVfxManagerCache();

			// Rebuild available actions to reflect any changes
			RebuildAvailableActions();

			// Clear cached preset
			cachedPreset = null;

			ShowNotification(new GUIContent("Data reloaded successfully"));
		}

		private void ReloadVfxPresetsData()
		{
			if (VfxManager.Instance == null)
			{
				VfxManager.EditorEnsureInstance();
			}

			if (VfxManager.Instance != null)
			{
				// Get the database assigned to the VfxManager
				var databaseField = typeof(VfxManager).GetField("database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (databaseField != null)
				{
					var database = databaseField.GetValue(VfxManager.Instance) as SkillVfxDatabase;
					if (database != null)
					{
						// Force refresh the database by marking it as dirty
						UnityEditor.EditorUtility.SetDirty(database);
						Debug.Log($"Reloaded VFX database: {database.name}");

						// Get the presets list from the database
						var presetsField = typeof(SkillVfxDatabase).GetField("presets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
						if (presetsField != null)
						{
							var presets = presetsField.GetValue(database) as System.Collections.Generic.List<SkillVfxPreset>;
							if (presets != null)
							{
								// Mark each preset as dirty to force refresh
								foreach (var preset in presets)
								{
									if (preset != null)
									{
										UnityEditor.EditorUtility.SetDirty(preset);
									}
								}
								Debug.Log($"Reloaded {presets.Count} VFX presets from database");
							}
						}
					}
					else
					{
						Debug.LogWarning("No SkillVfxDatabase assigned to VfxManager");
					}
				}
			}
		}

		private void ClearVfxManagerCache()
		{
			if (VfxManager.Instance != null)
			{
				// Clear the cached data in SkillVfxDatabase by forcing a refresh
				// We can do this by accessing the database and clearing its cache
				var databaseField = typeof(VfxManager).GetField("database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (databaseField != null)
				{
					var database = databaseField.GetValue(VfxManager.Instance) as SkillVfxDatabase;
					if (database != null)
					{
						// Clear the cached dictionary by setting it to null
						var cacheField = typeof(SkillVfxDatabase).GetField("cachedByName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
						if (cacheField != null)
						{
							cacheField.SetValue(database, null);
							Debug.Log("Cleared VfxManager cache");
						}
					}
				}
			}
		}
	}
}


