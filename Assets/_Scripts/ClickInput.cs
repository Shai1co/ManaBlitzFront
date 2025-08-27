
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace ManaGambit
{
    public interface ISkillBarUI
    {
        void BindUnit(Unit unit);
        void Clear();
    }

	public class ClickInput : MonoBehaviour
	{
		private const float RaycastMaxDistance = 200f;
		[SerializeField] private Camera sceneCamera;
		[SerializeField] private LayerMask slotLayer;
		[SerializeField] private LayerMask unitLayer;
		[SerializeField] private bool requestTargetsOnSelect = false; // disabled by default
		[SerializeField] private bool preferServerTargets = false; // if true, hide local targets once server targets arrive

		private Unit selectedUnit;
		[SerializeField] private bool debugInput = false;
		[SerializeField] private MonoBehaviour skillBarUI; // optional hook for UI updates; must implement ISkillBarUI
		private ISkillBarUI SkillBar => skillBarUI as ISkillBarUI;

		private void Awake()
		{
			if (sceneCamera == null) sceneCamera = Camera.main;
		}

		private void OnEnable()
		{
			// Ensure touch works in Editor Device Simulator and on mobile
			EnhancedTouchSupport.Enable();
			TouchSimulation.Enable();
			if (requestTargetsOnSelect && NetworkManager.Instance != null)
			{
				NetworkManager.Instance.OnValidTargets += HandleValidTargets;
			}
		}

		private void OnDisable()
		{
			TouchSimulation.Disable();
			EnhancedTouchSupport.Disable();
			if (requestTargetsOnSelect && NetworkManager.Instance != null)
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
				if (unit != null) { HandleUnitClick(unit); }
				return; // done handling this click
			}

			// Fallback: try any layer for Unit if layer mask misconfigured
			if (Physics.Raycast(ray, out RaycastHit anyUnitHit, RaycastMaxDistance, ~0, QueryTriggerInteraction.Collide))
			{
				var unitAny = anyUnitHit.collider.GetComponentInParent<Unit>();
				if (unitAny != null)
				{
					if (debugInput) Debug.LogWarning($"[ClickInput] Unit was not on unitLayer; using fallback hit '{anyUnitHit.collider.name}' on layer {anyUnitHit.collider.gameObject.layer}");
					HandleUnitClick(unitAny);
					return;
				}
			}

			// 2) If a unit is selected, try issue a move by clicking a slot
			if (selectedUnit != null && Physics.Raycast(ray, out RaycastHit slotHit, RaycastMaxDistance, slotLayer, QueryTriggerInteraction.Collide))
			{
				var slot = slotHit.collider.transform;
				if (debugInput)
				{
					Debug.Log($"[ClickInput] Slot hit '{slotHit.collider.name}' layer={slotHit.collider.gameObject.layer}");
				}
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
				if (TryIssueMoveToSlot(slotTransform))
				{
					if (debugInput) Debug.LogWarning($"[ClickInput] Slot was not on slotLayer; using fallback hit '{anySlotHit.collider.name}'");
					return;
				}
			}

			// 3) Otherwise, clear selection if clicked elsewhere
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
			selectedUnit = null;
			NetworkManager.Instance.HideAllHighlights();
			if (SkillBar != null)
			{
				SkillBar.Clear();
			}
		}

		private void HandleValidTargets(ValidTargetsEvent evt)
		{
			if (!requestTargetsOnSelect) return;
			if (selectedUnit == null || evt == null || evt.data == null) return;
			if (evt.data.unitId != selectedUnit.UnitID) return;
			int count = evt.data.targets != null ? evt.data.targets.Length : 0;
			if (debugInput) Debug.Log($"[ClickInput] Valid targets for {selectedUnit.UnitID}: {count}");
			if (preferServerTargets)
			{
				Board.Instance.HideAllHighlights();
			}
			if (count == 0) return;
			var coords = new List<Vector2Int>(count);
			for (int i = 0; i < count; i++)
			{
				var cell = evt.data.targets[i]?.cell;
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
						bs.SetHighlightVisible(true);
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
							bs.SetHighlightVisible(true);
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

			selectedUnit = unit;
			if (NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
			{
				var legal = LocalTargeting.ComputeMoveTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset);
				Board.Instance.HideAllHighlights();
				Board.Instance.HighlightSlots(legal, true);
				// Also compute basic skill (index 0) targets for preview using local helper
				var skillTargets = LocalTargeting.ComputeAttackTargets(selectedUnit, NetworkManager.Instance.UnitConfigAsset, 0);
				Board.Instance.HighlightSkillSlots(skillTargets, true);
				if (requestTargetsOnSelect && !string.IsNullOrEmpty(selectedUnit.UnitID))
				{
					NetworkManager.Instance.RequestValidTargets(selectedUnit.UnitID, "Move");
				}
			}
			else
			{
				if (NetworkManager.Instance != null) NetworkManager.Instance.ShowHighlightsForSelection();
			}
			if (SkillBar != null)
			{
				SkillBar.BindUnit(selectedUnit);
			}
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
					if (NetworkManager.Instance != null) NetworkManager.Instance.HideAllHighlights();
					return true;
				}
				else
				{
					Debug.LogWarning("[ClickInput] IntentManager.Instance is null or UnitID empty; cannot send Move intent.");
				}
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
				var fingerId = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].finger.index;
				return EventSystem.current.IsPointerOverGameObject(fingerId);
			}
			return EventSystem.current.IsPointerOverGameObject();
		}

		private string UnitConfigPieceIdOf(Unit unit)
		{
			// Not needed for server flow; left for future mapping
			return null;
		}

		private string FirstActionName(string pieceId)
		{
			return null;
		}
	}
}
