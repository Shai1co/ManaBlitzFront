using UnityEngine;
using UnityEngine.EventSystems;

namespace ManaGambit
{
	public class BoardSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		[SerializeField]
		private MeshRenderer highlightRenderer;

		[SerializeField]
		private MeshRenderer mouseHighlightRenderer;

		[SerializeField]
		private MeshRenderer skillHighlightRenderer;

		[SerializeField]
		private Unit occupant;

		public bool IsOccupied => occupant != null;
		public Unit Occupant => occupant;

		public bool IsHighlighted => highlightRenderer != null && highlightRenderer.enabled;
		public bool IsSkillHighlighted => skillHighlightRenderer != null && skillHighlightRenderer.enabled;

		public void ShowHighlight()
		{
			SetHighlightVisible(true);
		}

		public void HideHighlight()
		{
			SetHighlightVisible(false);
		}

		public void SetHighlightVisible(bool isVisible)
		{
			if (highlightRenderer == null)
			{
				EnsureSetup();
			}
			if (highlightRenderer != null)
			{
				highlightRenderer.enabled = isVisible;
			}
		}

		public void EnsureSetup()
		{
			// Setup selection highlight
			if (highlightRenderer == null)
			{
				var highlightChild = transform.Find("Highlight");
				if (highlightChild != null)
				{
					highlightRenderer = highlightChild.GetComponent<MeshRenderer>();
					if (highlightRenderer != null)
					{
						highlightRenderer.enabled = false;
					}
				}

				if (highlightRenderer == null)
				{
					var renderers = GetComponentsInChildren<MeshRenderer>(true);
					foreach (var r in renderers)
					{
						if (r.transform == transform) continue;
						highlightRenderer = r;
						break;
					}
				}

				if (highlightRenderer != null)
				{
					highlightRenderer.enabled = false;
				}
			}

			// Setup mouse highlight
			if (mouseHighlightRenderer == null)
			{
				var mouseChild = transform.Find("MouseHighlight");
				if (mouseChild != null)
				{
					mouseHighlightRenderer = mouseChild.GetComponent<MeshRenderer>();
					if (mouseHighlightRenderer != null)
					{
						mouseHighlightRenderer.enabled = false;
					}
				}
			}

			// Setup skill highlight (separate overlay)
			if (skillHighlightRenderer == null)
			{
				var skillChild = transform.Find("SkillHighlight");
				if (skillChild != null)
				{
					skillHighlightRenderer = skillChild.GetComponent<MeshRenderer>();
					if (skillHighlightRenderer != null)
					{
						skillHighlightRenderer.enabled = false;
					}
				}
			}
		}

		private void Awake()
		{
			EnsureSetup();
		}

		private void OnValidate()
		{
			// Keep the reference wired in editor and ensure it starts hidden
			if (highlightRenderer == null)
			{
				EnsureSetup();
			}
			else
			{
				highlightRenderer.enabled = false;
			}

			if (mouseHighlightRenderer != null)
			{
				mouseHighlightRenderer.enabled = false;
			}

			if (skillHighlightRenderer != null)
			{
				skillHighlightRenderer.enabled = false;
			}
		}

		public void ShowMouseHighlight()
		{
			SetMouseHighlightVisible(true);
		}

		public void HideMouseHighlight()
		{
			SetMouseHighlightVisible(false);
		}

		private void SetMouseHighlightVisible(bool isVisible)
		{
			if (mouseHighlightRenderer == null)
			{
				EnsureSetup();
			}
			if (mouseHighlightRenderer != null)
			{
				mouseHighlightRenderer.enabled = isVisible;
			}
		}

		public void ShowSkillHighlight()
		{
			SetSkillHighlightVisible(true);
		}

		public void HideSkillHighlight()
		{
			SetSkillHighlightVisible(false);
		}

		public void SetSkillHighlightVisible(bool isVisible)
		{
			if (skillHighlightRenderer == null)
			{
				EnsureSetup();
			}
			if (skillHighlightRenderer != null)
			{
				skillHighlightRenderer.enabled = isVisible;
			}
		}

		private void OnMouseEnter()
		{
			SetMouseHighlightVisible(true);
		}

		private void OnMouseExit()
		{
			SetMouseHighlightVisible(false);
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			SetMouseHighlightVisible(true);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			SetMouseHighlightVisible(false);
		}

		private void OnDisable()
		{
			// Ensure no highlight remains visible if object is disabled
			if (highlightRenderer != null) highlightRenderer.enabled = false;
			if (mouseHighlightRenderer != null) mouseHighlightRenderer.enabled = false;
			if (skillHighlightRenderer != null) skillHighlightRenderer.enabled = false;
		}

		public void SetOccupant(Unit unit)
		{
			occupant = unit;
		}

		public void ClearOccupant(Unit unit)
		{
			if (occupant == unit) occupant = null;
		}
	}
}


