using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;

namespace ManaGambit
{
	public class BoardHoverHighlighter : MonoBehaviour
	{
		[SerializeField]
		private Camera targetCamera;

		[SerializeField]
		private LayerMask slotLayerMask = ~0;

		[SerializeField]
		private float maxDistance = 100f;

		private BoardSlot lastHoveredSlot;
		[SerializeField] private bool debugHover = false;

		private void Awake()
		{
			if (targetCamera == null)
			{
				targetCamera = Camera.main;
			}
		}

		private void OnEnable()
		{
			EnhancedTouchSupport.Enable();
			TouchSimulation.Enable();
		}

		private void OnDisable()
		{
			TouchSimulation.Disable();
			EnhancedTouchSupport.Disable();
			SetHovered(null);
		}

		private void Update()
		{
			if (targetCamera == null) return;

			if (IsPointerOverUI())
			{
				SetHovered(null);
				return;
			}

			Vector2 screenPos = ReadPointerScreenPosition();
			if (!IsFinite(screenPos))
			{
				if (debugHover) Debug.LogWarning("[BoardHoverHighlighter] Non-finite screen position; skipping hover raycast.");
				SetHovered(null);
				return;
			}

			Ray ray = targetCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
			if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, slotLayerMask, QueryTriggerInteraction.Collide))
			{
				var slot = hit.collider.GetComponentInParent<BoardSlot>();
				if (slot != lastHoveredSlot)
				{
					SetHovered(slot);
				}
			}
			else
			{
				SetHovered(null);
			}
		}

		private void SetHovered(BoardSlot slot)
		{
			if (lastHoveredSlot != null && lastHoveredSlot != slot)
			{
				lastHoveredSlot.HideMouseHighlight();
			}
			lastHoveredSlot = slot;
			if (lastHoveredSlot != null)
			{
				lastHoveredSlot.ShowMouseHighlight();
			}
		}

		private static Vector2 ReadPointerScreenPosition()
		{
			if (EnhancedTouchSupport.enabled && UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
			{
				return UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].screenPosition;
			}
			if (Touchscreen.current != null)
			{
				return Touchscreen.current.primaryTouch.position.ReadValue();
			}
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
	}
}


