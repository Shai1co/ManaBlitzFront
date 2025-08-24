
using UnityEngine;
using UnityEngine.InputSystem;

namespace ManaGambit
{
	public class ClickInput : MonoBehaviour
	{
		private const float RaycastMaxDistance = 200f;
		[SerializeField] private Camera sceneCamera;
		[SerializeField] private LayerMask slotLayer;
		[SerializeField] private LayerMask unitLayer;
		[SerializeField] private bool requestTargetsOnSelect = false; // disabled by default

		private Unit selectedUnit;

		private void Awake()
		{
			if (sceneCamera == null) sceneCamera = Camera.main;
		}

		private void OnEnable()
		{
			if (requestTargetsOnSelect && NetworkManager.Instance != null)
			{
				NetworkManager.Instance.OnValidTargets += HandleValidTargets;
			}
		}

		private void OnDisable()
		{
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
			Vector2 pointerPos = ReadPointerScreenPosition();
			Ray ray = sceneCamera.ScreenPointToRay(new Vector3(pointerPos.x, pointerPos.y, 0f));

			// 1) Try select a unit first
			if (Physics.Raycast(ray, out RaycastHit unitHit, RaycastMaxDistance, unitLayer))
			{
				var unit = unitHit.collider.GetComponentInParent<Unit>();
				if (unit != null)
				{
					// If we already have a selected unit and clicked another unit that's not ours, treat as attack
					if (selectedUnit != null && unit != selectedUnit)
					{
						// Build a cell-targeted skill intent using first action index = 0
						if (Board.Instance.TryGetCoord(unit, out var enemyCoord))
						{
							var target = new SkillTarget { cell = new Pos { x = enemyCoord.x, y = enemyCoord.y } };
							if (IntentManager.Instance != null && !string.IsNullOrEmpty(selectedUnit.UnitID))
							{
								_ = IntentManager.Instance.SendUseSkillIntent(selectedUnit.UnitID, 0, target);
								if (NetworkManager.Instance != null) NetworkManager.Instance.HideAllHighlights();
							}
							else
							{
								Debug.LogWarning("[ClickInput] IntentManager.Instance is null or UnitID empty; cannot send UseSkill intent.");
							}
						}
						return;
					}

					selectedUnit = unit;
					if (requestTargetsOnSelect && !string.IsNullOrEmpty(selectedUnit.UnitID))
					{
						NetworkManager.Instance.RequestValidTargets(selectedUnit.UnitID, "Move");
					}
					NetworkManager.Instance.ShowHighlightsForSelection();
				}
				return; // done handling this click
			}

			// 2) If a unit is selected, try issue a move by clicking a slot
			if (selectedUnit != null && Physics.Raycast(ray, out RaycastHit slotHit, RaycastMaxDistance, slotLayer))
			{
				var slot = slotHit.collider.transform;
				if (Board.Instance.TryGetCoord(slot, out var toCoord))
				{
					var fromCoord = selectedUnit.CurrentPosition;
					var from = new Pos { x = fromCoord.x, y = fromCoord.y };
					var to = new Pos { x = toCoord.x, y = toCoord.y };
					if (IntentManager.Instance != null && !string.IsNullOrEmpty(selectedUnit.UnitID))
					{
						_ = IntentManager.Instance.SendMoveIntent(selectedUnit.UnitID, from, to);
						if (NetworkManager.Instance != null) NetworkManager.Instance.HideAllHighlights();
					}
					else
					{
						Debug.LogWarning("[ClickInput] IntentManager.Instance is null or UnitID empty; cannot send Move intent.");
					}
				}
				return;
			}

			// 3) Otherwise, clear selection if clicked elsewhere
			selectedUnit = null;
			NetworkManager.Instance.HideAllHighlights();
		}

		private void HandleValidTargets(ValidTargetsEvent evt)
		{
			if (!requestTargetsOnSelect) return;
			if (selectedUnit == null || evt == null || evt.data == null) return;
			if (evt.data.unitId != selectedUnit.UnitID) return;
			int count = evt.data.targets != null ? evt.data.targets.Length : 0;
			Debug.Log($"[ClickInput] Valid targets for {selectedUnit.UnitID}: {count}");
			// Hook into your highlighter here if available
		}

		private static bool WasPrimaryPressedThisFrame()
		{
			// Mouse
			if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
			{
				return true;
			}
			// Touch
			if (Touchscreen.current != null)
			{
				var touch = Touchscreen.current.primaryTouch;
				if (touch.press.wasPressedThisFrame)
				{
					return true;
				}
			}
			return false;
		}

		private static Vector2 ReadPointerScreenPosition()
		{
			if (Mouse.current != null)
			{
				return Mouse.current.position.ReadValue();
			}
			if (Touchscreen.current != null)
			{
				return Touchscreen.current.primaryTouch.position.ReadValue();
			}
			return Vector2.zero;
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
