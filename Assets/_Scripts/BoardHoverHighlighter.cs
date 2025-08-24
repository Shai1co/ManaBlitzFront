using UnityEngine;
using UnityEngine.InputSystem;

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

		private void Awake()
		{
			if (targetCamera == null)
			{
				targetCamera = Camera.main;
			}
		}

		private void Update()
		{
			if (targetCamera == null) return;

			Vector2 screenPos;
			if (Mouse.current != null)
			{
				screenPos = Mouse.current.position.ReadValue();
			}
			else if (Pointer.current != null)
			{
				screenPos = Pointer.current.position.ReadValue();
			}
			else
			{
				return;
			}

			Ray ray = targetCamera.ScreenPointToRay(screenPos);
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

		private void OnDisable()
		{
			SetHovered(null);
		}
	}
}


