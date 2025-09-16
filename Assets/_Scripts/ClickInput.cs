
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using HighlightPlus;

namespace ManaGambit
{
    public interface ISkillBarUI
    {
        void BindUnit(Unit unit);
        void Clear();
        void SetSelectedSkillIndex(int index);
    }

	public partial class ClickInput : MonoBehaviour
	{
		private const float RaycastMaxDistance = 200f;
		private const int DefaultSkillIndex = 0;
		private const int NoSkillIndex = -1; // sentinel for no selected skill
		[SerializeField] private Camera sceneCamera;
		[SerializeField] private LayerMask slotLayer;
		[SerializeField] private LayerMask unitLayer;
		[SerializeField] private bool requestTargetsOnSelect = false; // JS client: no server request; keep local default off
		[SerializeField] private bool preferServerTargets = false; // if true, hide local targets once server targets arrive
		[SerializeField] private bool requestTargetsOnSkillTargeting = false; // JS client computes locally; default off
		[SerializeField] private bool cancelTargetingOnMiss = true;

		private Unit selectedUnit;
		private Unit targetingUnit;
		private bool isTargetingSkill = false;
		private int pendingSkillIndex = NoSkillIndex;
		private int currentSelectedSkillIndex = NoSkillIndex;
		[SerializeField] private bool debugInput = false;
		[SerializeField] private MonoBehaviour skillBarUI; // optional hook for UI updates; must implement ISkillBarUI
		private ISkillBarUI SkillBar => skillBarUI as ISkillBarUI;
		private Unit highlightHookUnit; // unit we've attached highlight event hooks to
		// Guard to prevent duplicate highlight redraws in the same frame
		private static readonly ManaGambit.OncePerFrameGate<Unit> _redrawGate = new ManaGambit.OncePerFrameGate<Unit>();

		private void Awake()
		{
			if (sceneCamera == null) sceneCamera = Camera.main;
			if (skillBarUI == null)
			{
				var found = FindFirstObjectByType<SkillBarUI>();
				if (found != null) skillBarUI = found;
			}
		}

		private void OnEnable()
		{
			// Ensure touch works in Editor Device Simulator and on mobile
			EnhancedTouchSupport.Enable();
			TouchSimulation.Enable();
			if ((requestTargetsOnSelect || requestTargetsOnSkillTargeting) && NetworkManager.Instance != null && NetworkManager.Instance.ServerSupportsValidTargets)
			{
				NetworkManager.Instance.OnValidTargets += HandleValidTargets;
			}
		}

		private void OnDisable()
		{
			TouchSimulation.Disable();
			EnhancedTouchSupport.Disable();
			if ((requestTargetsOnSelect || requestTargetsOnSkillTargeting) && NetworkManager.Instance != null && NetworkManager.Instance.ServerSupportsValidTargets)
			{
				NetworkManager.Instance.OnValidTargets -= HandleValidTargets;
			}
		}

		private void Update()
		{
			if (sceneCamera == null) return;

			if (WasPrimaryPressedThisFrame())
			{
				HandleLeftClickUnified();
			}
		}

		private void HandleLeftClickUnified()
		{
			// Prevent issuing commands through UI elements
			if (IsPointerOverUI())
			{
				return;
			}
			Vector2 pointerPos = ReadPointerScreenPosition();
			if (!IsFinite(pointerPos))
			{
				if (debugInput) Debug.LogWarning("[ClickInput] Non-finite screen position; skipping click");
				return;
			}
			Ray ray = sceneCamera.ScreenPointToRay(new Vector3(pointerPos.x, pointerPos.y, 0f));
			if (debugInput)
			{
				Debug.Log($"[ClickInput] Press at {pointerPos}, overUI={IsPointerOverUI()}");
			}

			// 1) Try select a unit first
			if (Physics.Raycast(ray, out RaycastHit unitHit, RaycastMaxDistance, unitLayer, QueryTriggerInteraction.Collide))
			{
				var unit = unitHit.collider.GetComponentInParent<Unit>();
				if (debugInput)
				{
					Debug.Log($"[ClickInput] Unit hit '{unitHit.collider.name}' layer={unitHit.collider.gameObject.layer} unit={(unit!=null)}");
				}
				if (unit != null)
				{
					// Never outline enemies upon click
					bool isOwnRelativeToLocal = AuthManager.Instance != null && string.Equals(unit.OwnerId, AuthManager.Instance.UserId);
					if (!isOwnRelativeToLocal)
					{
						SetUnitSelectionHighlight(unit, false);
					}
					// If targeting a skill and clicked an enemy unit that is a valid target, cast instead of selecting
					if (isTargetingSkill && targetingUnit != null)
					{
						if (TryExecuteSkillOnUnit(targetingUnit, unit, pendingSkillIndex))
						{
							isTargetingSkill = false;
							pendingSkillIndex = NoSkillIndex;
							// Clear both move and skill highlights on cast start
							Board.Instance.ClearMoveHighlights();
							Board.Instance.ClearSkillHighlights();
							// Clear the selected skill after use
							currentSelectedSkillIndex = NoSkillIndex;
							if (SkillBar != null) SkillBar.SetSelectedSkillIndex(currentSelectedSkillIndex);
							// Restore move/skill previews after the skill completes
							void RestoreAfterSkill(Unit u)
							{
								u.OnSkillCompleted -= RestoreAfterSkill;
								if (selectedUnit != null && NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
								{
									var legal = LocalTargeting.ComputeMoveTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset);
									Board.Instance.HighlightSlots(legal, true);
									var skillTargets = LocalTargeting.ComputeAttackTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset, currentSelectedSkillIndex);
									Board.Instance.HighlightSkillSlots(skillTargets, true);
								}
							}
							targetingUnit.OnSkillCompleted += RestoreAfterSkill;
							return;
						}
					}
					// If not explicitly targeting a skill, mirror JS: use default skill (index 0) on enemy click when valid
					if (!isTargetingSkill && selectedUnit != null)
					{
						const int defaultSkillIndex = 0;
						if (TryExecuteSkillOnUnit(selectedUnit, unit, defaultSkillIndex))
						{
							return;
						}
					}
					HandleUnitClick(unit);
				}
				return; // done handling this click
			}

			// Fallback: try any layer for Unit if layer mask misconfigured
			if (Physics.Raycast(ray, out RaycastHit anyUnitHit, RaycastMaxDistance, ~0, QueryTriggerInteraction.Collide))
			{
				var unitAny = anyUnitHit.collider.GetComponentInParent<Unit>();
				if (unitAny != null)
				{
					if (debugInput) Debug.LogWarning($"[ClickInput] Unit was not on unitLayer; using fallback hit '{anyUnitHit.collider.name}' on layer {anyUnitHit.collider.gameObject.layer}");
					// Never outline enemies upon click (fallback path)
					bool isOwnRelativeToLocal = AuthManager.Instance != null && string.Equals(unitAny.OwnerId, AuthManager.Instance.UserId);
					if (!isOwnRelativeToLocal)
					{
						SetUnitSelectionHighlight(unitAny, false);
					}
					HandleUnitClick(unitAny);
					return;
				}
			}

			// 2) If targeting a skill, interpret slot click as skill target
			if (isTargetingSkill && Physics.Raycast(ray, out RaycastHit skillSlotHit, RaycastMaxDistance, slotLayer, QueryTriggerInteraction.Collide))
			{
				var slot = skillSlotHit.collider.transform;
				if (TryIssueSkillAtSlot(slot)) { return; }
			}

			// Fallback: try any layer for slot when targeting a skill
			if (isTargetingSkill && Physics.Raycast(ray, out RaycastHit anySkillSlotHit, RaycastMaxDistance, ~0, QueryTriggerInteraction.Collide))
			{
				var slotTransform = anySkillSlotHit.collider.transform;
				if (!Board.Instance.TryGetCoord(slotTransform, out var _))
				{
					var boardSlot = anySkillSlotHit.collider.GetComponentInParent<BoardSlot>();
					if (boardSlot != null) slotTransform = boardSlot.transform;
				}
				if (TryIssueSkillAtSlot(slotTransform))
				{
					if (debugInput) Debug.LogWarning($"[ClickInput] (Targeting) Slot was not on slotLayer; using fallback hit '{anySkillSlotHit.collider.name}'");
					return;
				}
			}

			// 3) If a unit is selected, try issue a move by clicking a slot OR select unit if slot is occupied
			if (selectedUnit != null && Physics.Raycast(ray, out RaycastHit slotHit, RaycastMaxDistance, slotLayer, QueryTriggerInteraction.Collide))
			{
				var slot = slotHit.collider.transform;
				if (debugInput)
				{
					Debug.Log($"[ClickInput] Slot hit '{slotHit.collider.name}' layer={slotHit.collider.gameObject.layer}");
				}
				
				// Check if slot is occupied by a unit - if so, select that unit instead
				if (Board.Instance.TryGetCoord(slot, out var slotCoord))
				{
					if (Board.Instance.TryGetBoardSlot(slotCoord, out var boardSlot) && boardSlot.IsOccupied)
					{
						var occupantUnit = boardSlot.Occupant;
						if (occupantUnit != null)
						{
							HandleUnitClick(occupantUnit);
							return;
						}
					}
				}
				
				// If not explicitly targeting a skill, allow default/basic skill (index 0) by clicking a valid red-highlighted slot
				if (!isTargetingSkill && TryExecuteDefaultSkillOnSlot(slot, selectedUnit)) { return; }
				if (TryIssueMoveToSlot(slot)) { return; }
				return;
			}

			// Fallback: try any layer for slot when a unit is selected
			if (selectedUnit != null && Physics.Raycast(ray, out RaycastHit anySlotHit, RaycastMaxDistance, ~0, QueryTriggerInteraction.Collide))
			{
				var slotTransform = anySlotHit.collider.transform;
				// Try parent BoardSlot if direct transform doesn't map
				if (!Board.Instance.TryGetCoord(slotTransform, out var _))
				{
					var boardSlot = anySlotHit.collider.GetComponentInParent<BoardSlot>();
					if (boardSlot != null) slotTransform = boardSlot.transform;
				}
				
				// Check if slot is occupied by a unit - if so, select that unit instead
				if (Board.Instance.TryGetCoord(slotTransform, out var slotCoord))
				{
					if (Board.Instance.TryGetBoardSlot(slotCoord, out var boardSlot) && boardSlot.IsOccupied)
					{
						var occupantUnit = boardSlot.Occupant;
						if (occupantUnit != null)
						{
							HandleUnitClick(occupantUnit);
							if (debugInput) Debug.LogWarning($"[ClickInput] Slot was not on slotLayer; using fallback hit '{anySlotHit.collider.name}' to select unit");
							return;
						}
					}
				}
				
				// If not explicitly targeting a skill, allow default/basic skill (index 0) by clicking a valid red-highlighted slot
				if (!isTargetingSkill && TryExecuteDefaultSkillOnSlot(slotTransform, selectedUnit)) { return; }
				if (TryIssueMoveToSlot(slotTransform))
				{
					if (debugInput) Debug.LogWarning($"[ClickInput] Slot was not on slotLayer; using fallback hit '{anySlotHit.collider.name}'");
					return;
				}
			}

			// 4) Otherwise, clear or cancel depending on context
			if (debugInput)
			{
				if (Physics.Raycast(ray, out var anyHit, RaycastMaxDistance, ~0))
				{
					Debug.Log($"[ClickInput] Missed masks; actually hit '{anyHit.collider.name}' on layer {anyHit.collider.gameObject.layer}");
				}
				else
				{
					Debug.Log("[ClickInput] Raycast hit nothing. Check camera/colliders/layers.");
				}
			}
			if (isTargetingSkill && cancelTargetingOnMiss)
			{
				if (debugInput) Debug.Log("[ClickInput] Cancel targeting on miss (keeping unit selected)");
				isTargetingSkill = false;
				pendingSkillIndex = NoSkillIndex;
				Board.Instance.ClearSkillHighlights();
				// Keep the unit selected; do not clear selection/highlights fully
				return;
			}
			// Do not auto-deselect unit on miss; mirror JS behavior of persistent selection
			if (debugInput) Debug.Log("[ClickInput] Miss with no selection change; keeping current selection");
		}

		private void HandleValidTargets(ValidTargetsEvent evt)
		{
			// Accept valid targets for either selection or skill targeting flows
			if (selectedUnit == null || evt == null || evt.data == null) return;
			if (evt.data.unitId != selectedUnit.UnitID) return;
			int count = evt.data.targets != null ? evt.data.targets.Length : 0;
			if (debugInput) Debug.Log($"[ClickInput] Valid targets for {selectedUnit.UnitID}: {count}");
			if (preferServerTargets)
			{
				if (isTargetingSkill) Board.Instance.ClearSkillHighlights(); else Board.Instance.ClearMoveHighlights();
			}
			if (count == 0) return;
			var coords = new List<Vector2Int>(count);
			for (int i = 0; i < count; i++)
			{
				var target = evt.data.targets[i];
				if (target == null) continue;
				var cell = target.cell;
				if (cell == null) continue;
				var v = new Vector2Int(cell.x, cell.y);
				coords.Add(v);
				if (debugInput) Debug.Log($"[ClickInput] -> target cell {v.x},{v.y}");
			}
			if (coords.Count > 0)
			{
				int shown = 0;
				for (int i = 0; i < coords.Count; i++)
				{
					var c = coords[i];
					if (Board.Instance.TryGetBoardSlot(c, out var bs))
					{
						if (isTargetingSkill) bs.SetSkillHighlightVisible(true); else bs.SetHighlightVisible(true);
						shown++;
					}
				}
				if (shown == 0)
				{
					// Try swapped coordinates as a fallback if server orientation differs
					int swappedShown = 0;
					for (int i = 0; i < coords.Count; i++)
					{
						var c = coords[i];
						var s = new Vector2Int(c.y, c.x);
						if (Board.Instance.TryGetBoardSlot(s, out var bs))
						{
							if (isTargetingSkill) bs.SetSkillHighlightVisible(true); else bs.SetHighlightVisible(true);
							swappedShown++;
						}
					}
					if (debugInput) Debug.Log($"[ClickInput] No exact coord matches; swappedShown={swappedShown}");
				}
				else if (debugInput)
				{
					Debug.Log($"[ClickInput] Shown highlights: {shown}");
				}
			}
		}

		private void HandleUnitClick(Unit unit)
		{
			bool isOwn = AuthManager.Instance != null && string.Equals(unit.OwnerId, AuthManager.Instance.UserId);
			if (!isOwn)
			{
				if (debugInput) Debug.Log("[ClickInput] Clicked enemy unit; not auto-attacking (disabled to avoid server error).");
				return;
			}

			// Toggle highlight between previous and new selection
			var previous = selectedUnit;
			selectedUnit = unit;
			if (previous != null && previous != selectedUnit)
			{
				SetUnitSelectionHighlight(previous, false);
				DetachUnitHighlightHooks();
			}
			SetUnitSelectionHighlight(selectedUnit, true);
			AttachUnitHighlightHooks(selectedUnit);
			// Reset explicit targeting state; only clear selected skill when switching units
			isTargetingSkill = false;
			pendingSkillIndex = NoSkillIndex;
			if (previous != selectedUnit)
			{
				currentSelectedSkillIndex = NoSkillIndex;
			}
			RefreshHighlightsForSelectedUnit();
			if (SkillBar != null)
			{
				SkillBar.BindUnit(selectedUnit);
				SkillBar.SetSelectedSkillIndex(currentSelectedSkillIndex);
			}
		}

		private void RefreshHighlightsForSelectedUnit()
		{
			if (selectedUnit == null)
			{
				Board.Instance.HideAllHighlights();
				return;
			}
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			if (cfg == null)
			{
				if (NetworkManager.Instance != null) NetworkManager.Instance.ShowHighlightsForSelection();
				return;
			}
			// Ensure UnitConfig is populated
			try
			{
				if (cfg.units == null || cfg.units.Length == 0)
				{
					cfg.LoadFromJson();
					if (debugInput) Debug.Log("[ClickInput] UnitConfig was empty; loaded JSON for local targeting.");
				}
			}
			catch { }
			// Hide, then show move and skill highlights for currentSelectedSkillIndex
			Board.Instance.HideAllHighlights();
			var legal = LocalTargeting.ComputeMoveTargets(selectedUnit, cfg);
			Board.Instance.HighlightSlots(legal, true);
			var skillTargets = LocalTargeting.ComputeAttackTargets(selectedUnit, cfg, currentSelectedSkillIndex);
			Board.Instance.HighlightSkillSlots(skillTargets, true);
			if (requestTargetsOnSelect && NetworkManager.Instance != null && NetworkManager.Instance.ServerSupportsValidTargets && !string.IsNullOrEmpty(selectedUnit.UnitID))
			{
				NetworkManager.Instance.RequestValidTargets(selectedUnit.UnitID, "Move");
			}
		}

		private void AttachUnitHighlightHooks(Unit unit)
		{
			if (unit == null) return;
			DetachUnitHighlightHooks();
			highlightHookUnit = unit;
			unit.OnMoveStarted += OnUnitMoveStarted;
			unit.OnMoveCompleted += OnUnitMoveCompleted;
			unit.OnSkillStarted += OnUnitSkillStarted;
			unit.OnSkillCompleted += OnUnitSkillCompleted;
		}

		private void DetachUnitHighlightHooks()
		{
			if (highlightHookUnit == null) return;
			highlightHookUnit.OnMoveStarted -= OnUnitMoveStarted;
			highlightHookUnit.OnMoveCompleted -= OnUnitMoveCompleted;
			highlightHookUnit.OnSkillStarted -= OnUnitSkillStarted;
			highlightHookUnit.OnSkillCompleted -= OnUnitSkillCompleted;
			highlightHookUnit = null;
		}

	private void OnUnitMoveStarted(Unit u)
	{
		// Highlights are already cleared when the move intent is sent in TryIssueMoveToSlot()
		// No need to clear them again here, as it causes a blinking effect
	}

	private void OnUnitSkillStarted(Unit u)
	{
		// Highlights are already cleared when the skill intent is sent in TryIssueSkillAtSlot() 
		// and other skill execution methods. No need to clear them again here, as it causes a blinking effect
	}

		private void OnUnitMoveCompleted(Unit u)
		{
			if (u != selectedUnit) return;
			if (!_redrawGate.ShouldRun(u)) return;
			RefreshHighlightsForSelectedUnit();
		}
		private void OnUnitSkillCompleted(Unit u)
		{
			if (u != selectedUnit) return;
			if (!_redrawGate.ShouldRun(u)) return;
			RefreshHighlightsForSelectedUnit();
		}

		private bool TryIssueMoveToSlot(Transform slot)
		{
			if (selectedUnit == null || slot == null) return false;
			if (Board.Instance.TryGetCoord(slot, out var toCoord))
			{
				var fromCoord = selectedUnit.CurrentPosition;
				var from = new Pos { x = fromCoord.x, y = fromCoord.y };
				var to = new Pos { x = toCoord.x, y = toCoord.y };
				// Only allow moving own unit and to legal target cells (client-side validation)
				bool isOwn = AuthManager.Instance != null && string.Equals(selectedUnit.OwnerId, AuthManager.Instance.UserId);
				if (!isOwn)
				{
					if (debugInput) Debug.LogWarning("[ClickInput] Attempted to move an enemy unit; ignoring.");
					return false;
				}
				if (NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
				{
					var legal = LocalTargeting.ComputeMoveTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset);
					if (!legal.Contains(new Vector2Int(to.x, to.y)))
					{
						if (debugInput) Debug.LogWarning($"[ClickInput] Tile {to.x},{to.y} is not a legal move target; ignoring.");
						return false;
					}
				}
				if (IntentManager.Instance != null && !string.IsNullOrEmpty(selectedUnit.UnitID))
				{
					_ = IntentManager.Instance.SendMoveIntent(selectedUnit.UnitID, from, to);
					// Clear highlights immediately when the move starts; redraw on completion via Unit events
					Board.Instance.ClearMoveHighlights();
					Board.Instance.ClearSkillHighlights();
					// Highlights are cleared on move start and restored on completion via unit event hooks.
					return true;
				}
				else
				{
					Debug.LogWarning("[ClickInput] IntentManager.Instance is null or UnitID empty; cannot send Move intent.");
				}
			}
			return false;
		}

		public bool BeginSkillTargeting(Unit unit, int skillIndex)
		{
			if (unit == null) return false;
			bool isOwn = AuthManager.Instance != null && string.Equals(unit.OwnerId, AuthManager.Instance.UserId);
			if (!isOwn) return false;
			targetingUnit = unit;
			selectedUnit = unit;
			isTargetingSkill = true;
			pendingSkillIndex = skillIndex;
			currentSelectedSkillIndex = skillIndex;
			if (debugInput) Debug.Log($"[ClickInput] BeginSkillTargeting unit={unit.UnitID} skillIndex={skillIndex}");
			if (NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
			{
				var skillTargets = LocalTargeting.ComputeAttackTargets(targetingUnit, NetworkManager.Instance.UnitConfigAsset, skillIndex);
				Board.Instance.ClearSkillHighlights();
				// Exclusive skill preview: clear movement overlays like JS client when a skill is explicitly selected
				Board.Instance.ClearMoveHighlights();
				Board.Instance.HighlightSkillSlots(skillTargets, true);
			}
			if (requestTargetsOnSkillTargeting && NetworkManager.Instance != null && NetworkManager.Instance.ServerSupportsValidTargets && !string.IsNullOrEmpty(unit.UnitID))
			{
				NetworkManager.Instance.RequestValidTargets(unit.UnitID, "UseSkill", skillIndex);
			}
			// Notify UI about the newly selected skill index
			if (SkillBar != null)
			{
				SkillBar.SetSelectedSkillIndex(currentSelectedSkillIndex);
			}
			return true;
		}

		private bool IsSlotEmpty(Vector2Int coord)
		{
			if (!Board.Instance.TryGetBoardSlot(coord, out var bs) || bs == null)
				return false; // unknown slot => do not treat as empty
			return !bs.IsOccupied;
		}

		private bool ShouldPreferMoveOverSkill(Vector2Int coord)
		{
			// Check if the target cell is empty
			if (!IsSlotEmpty(coord)) return false;
			
			// Check if it's also a valid move target for the selected unit
			if (selectedUnit == null || NetworkManager.Instance?.UnitConfigAsset == null) return false;
			
			var legalMoveTargets = LocalTargeting.ComputeMoveTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset);
			return legalMoveTargets.Contains(coord);
		}

		private bool TryIssueSkillAtSlot(Transform slot)
		{
			if (!isTargetingSkill || slot == null || targetingUnit == null) return false;
			if (!Board.Instance.TryGetCoord(slot, out var toCoord)) return false;
			
			// JS behavior: if empty and also a valid move target, prefer moving instead of casting
			if (ShouldPreferMoveOverSkill(toCoord))
			{
				if (debugInput) Debug.Log($"[ClickInput] Target cell empty and valid move; issuing Move to {toCoord}");
				return TryIssueMoveToSlot(slot);
			}
			
			if (IntentManager.Instance != null && !string.IsNullOrEmpty(targetingUnit.UnitID))
			{
				if (debugInput) Debug.Log($"[ClickInput] Send UseSkill unit={targetingUnit.UnitID} skillIndex={pendingSkillIndex} target=({toCoord.x},{toCoord.y})");
				var target = new SkillTarget { cell = new Pos { x = toCoord.x, y = toCoord.y } };
				_ = IntentManager.Instance.SendUseSkillIntent(targetingUnit.UnitID, pendingSkillIndex, target);
				// Clear highlights at the start of skill usage
				Board.Instance.ClearMoveHighlights();
				Board.Instance.ClearSkillHighlights();
				// Keep unit selected; exit explicit targeting state and clear selected skill after use
				isTargetingSkill = false;
				pendingSkillIndex = NoSkillIndex;
				currentSelectedSkillIndex = NoSkillIndex;
				if (SkillBar != null) SkillBar.SetSelectedSkillIndex(currentSelectedSkillIndex);
				// Subscribe to restore overlays after skill completes
				void RestoreAfterSkill(Unit u)
				{
					u.OnSkillCompleted -= RestoreAfterSkill;
					if (selectedUnit != null && NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
					{
						var legal = LocalTargeting.ComputeMoveTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset);
						Board.Instance.HighlightSlots(legal, true);
						var skillTargets = LocalTargeting.ComputeAttackTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset, currentSelectedSkillIndex);
						Board.Instance.HighlightSkillSlots(skillTargets, true);
					}
				}
				targetingUnit.OnSkillCompleted += RestoreAfterSkill;
				return true;
			}
			return false;
		}

		private static bool WasPrimaryPressedThisFrame()
		{
			// Prefer Enhanced Touch when available (works best in Device Simulator)
			if (EnhancedTouchSupport.enabled)
			{
				for (int i = 0; i < UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count; i++)
				{
					var t = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[i];
					if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
					{
						return true;
					}
				}
			}

			// Fallback to Touchscreen device
			if (Touchscreen.current != null)
			{
				var touch = Touchscreen.current.primaryTouch;
				if (touch.press.wasPressedThisFrame)
				{
					return true;
				}
				// Any touch
				var touches = Touchscreen.current.touches;
				for (int i = 0; i < touches.Count; i++)
				{
					if (touches[i].press.wasPressedThisFrame) return true;
				}
			}

			// Pointer (covers mouse/pen)
			if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
			{
				return true;
			}
			if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
			{
				return true;
			}
			return false;
		}

		private static Vector2 ReadPointerScreenPosition()
		{
			// Enhanced Touch first
			if (EnhancedTouchSupport.enabled)
			{
				var active = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
				if (active.Count > 0)
				{
					return active[0].screenPosition;
				}
			}

			// Touchscreen
			if (Touchscreen.current != null)
			{
				return Touchscreen.current.primaryTouch.position.ReadValue();
			}

			// Pointer/mouse fallback
			if (Pointer.current != null)
			{
				return Pointer.current.position.ReadValue();
			}
			if (Mouse.current != null)
			{
				return Mouse.current.position.ReadValue();
			}
			return Vector2.positiveInfinity;
		}

		private static bool IsFinite(Vector2 v)
		{
			return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));
		}

	private static bool IsPointerOverUI()
	{
		if (EventSystem.current == null) return false;
		if (EnhancedTouchSupport.enabled && UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
		{
			var touch = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0];
			return EventSystem.current.IsPointerOverGameObject(touch.touchId);
		}
		return EventSystem.current.IsPointerOverGameObject();
	}
		private static void SetUnitSelectionHighlight(Unit unit, bool highlighted)
		{
			if (unit == null) return;
			
			var effect = unit.GetComponentInChildren<HighlightEffect>(true);
			if (effect == null) return;
			
			effect.SetHighlighted(highlighted);
		}

		private bool TryExecuteSkillOnUnit(Unit caster, Unit target, int skillIndex)
		{
			if (caster == null || target == null) return false;

			// Check if target is an enemy unit (relative to caster, not local user)
			bool isEnemyUnit = !string.Equals(target.OwnerId, caster.OwnerId);
			if (!isEnemyUnit) 
			{
				if (debugInput) Debug.Log($"[ClickInput] Target {target.UnitID} is not an enemy of caster {caster.UnitID} (target owner: {target.OwnerId}, caster owner: {caster.OwnerId})");
				return false;
			}

			var cfg = NetworkManager.Instance?.UnitConfigAsset;
			if (cfg == null) return false;

			var targetCell = target.CurrentPosition;
			var skillTargets = LocalTargeting.ComputeSkillTargets(caster, cfg, skillIndex);

			if (skillTargets.Contains(targetCell))
			{
				// Ensure enemies are not outlined when targeted/attacked
				SetUnitSelectionHighlight(target, false);
				// Guard: ensure caster has valid UnitID and IntentManager is available
				if (string.IsNullOrEmpty(caster.UnitID))
				{
					if (debugInput) Debug.Log($"[ClickInput] Cannot send skill intent: caster {caster.name} has empty UnitID");
					return false;
				}
				
				if (IntentManager.Instance == null)
				{
					if (debugInput) Debug.Log($"[ClickInput] Cannot send skill intent: IntentManager.Instance is null");
					return false;
				}

				if (debugInput) Debug.Log($"[ClickInput] Enemy unit {target.UnitID} clicked is valid skill target for caster {caster.UnitID}; sending UseSkill to {targetCell} with skillIndex {skillIndex}");
				var skillTarget = new SkillTarget { cell = new Pos { x = targetCell.x, y = targetCell.y } };
				_ = IntentManager.Instance.SendUseSkillIntent(caster.UnitID, skillIndex, skillTarget);
				return true;
			}
			
			if (debugInput) Debug.Log($"[ClickInput] Target cell {targetCell} is not a valid skill target for caster {caster.UnitID} with skill {skillIndex}");
			return false;
		}

		private bool TryExecuteDefaultSkillOnSlot(Transform slotTransform, Unit selectedUnit)
		{
			if (slotTransform == null || selectedUnit == null) return false;
			if (!Board.Instance.TryGetCoord(slotTransform, out var toCoord)) return false;
			
			// First check if this is an empty legal move target - if so, prefer move over default skill
			if (IsSlotEmpty(toCoord))
			{
				var moveCfg = NetworkManager.Instance?.UnitConfigAsset;
				if (moveCfg != null)
				{
					var legalMoveTargets = LocalTargeting.ComputeMoveTargets(selectedUnit, moveCfg);
					if (legalMoveTargets.Contains(toCoord))
					{
						if (debugInput) Debug.Log($"[ClickInput] Empty legal move target detected at {toCoord}, sending Move intent instead of default skill");
						if (IntentManager.Instance != null && !string.IsNullOrEmpty(selectedUnit.UnitID))
						{
							var fromCoord = selectedUnit.CurrentPosition;
							var from = new Pos { x = fromCoord.x, y = fromCoord.y };
							var to = new Pos { x = toCoord.x, y = toCoord.y };
							_ = IntentManager.Instance.SendMoveIntent(selectedUnit.UnitID, from, to);
							// Clear highlights immediately when the move starts
							Board.Instance.ClearMoveHighlights();
							Board.Instance.ClearSkillHighlights();
							return true;
						}
					}
				}
			}
			
			// Fallback to default skill logic if not a legal move target
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			if (cfg == null) return false;
			var defaultSkillTargets = LocalTargeting.ComputeSkillTargets(selectedUnit, cfg, DefaultSkillIndex);
			if (!defaultSkillTargets.Contains(toCoord)) return false;
			if (IntentManager.Instance == null || string.IsNullOrEmpty(selectedUnit.UnitID)) return false;
			if (debugInput) Debug.Log($"[ClickInput] Default skill ({DefaultSkillIndex}) at slot {toCoord}");
			var target = new SkillTarget { cell = new Pos { x = toCoord.x, y = toCoord.y } };
			_ = IntentManager.Instance.SendUseSkillIntent(selectedUnit.UnitID, DefaultSkillIndex, target);
			return true;
		}
	}
}
