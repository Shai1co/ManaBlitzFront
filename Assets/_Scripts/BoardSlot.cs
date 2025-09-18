using UnityEngine;
using UnityEngine.EventSystems;

namespace ManaGambit
{
	public class BoardSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		private const string MoveHighlightChildName = "Highlight";
		private const string SkillHighlightChildName = "SkillHighlight";
		private static readonly Color DefaultSkillHighlightColor = new Color(1f, 0f, 0f, 0.4f);

		[SerializeField]
		private MeshRenderer highlightRenderer; // green highlight

		[SerializeField]
		private MeshRenderer mouseHighlightRenderer;

		[SerializeField]
		private MeshRenderer skillHighlightRenderer; // red highlight

		[SerializeField]
		private Color skillHighlightColor = new Color(1f, 0f, 0f, 0.4f);

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
				// Mutual exclusivity: enabling move highlight disables skill highlight on the same slot
				if (isVisible && skillHighlightRenderer != null)
				{
					skillHighlightRenderer.enabled = false;
				}
			}
		}

		public void EnsureSetup()
		{
			// Setup selection highlight
			if (highlightRenderer == null)
			{
				var highlightChild = transform.Find(MoveHighlightChildName);
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
				var skillChild = transform.Find(SkillHighlightChildName);
				if (skillChild != null)
				{
					skillHighlightRenderer = skillChild.GetComponent<MeshRenderer>();
					if (skillHighlightRenderer != null)
					{
						skillHighlightRenderer.enabled = false;
					}
				}
				// If no dedicated skill highlight exists, auto-create one by cloning the move highlight
				if (skillHighlightRenderer == null && highlightRenderer != null)
				{
					var clone = Instantiate(highlightRenderer.gameObject, transform);
					clone.name = SkillHighlightChildName;
					clone.transform.localPosition = highlightRenderer.transform.localPosition;
					clone.transform.localRotation = highlightRenderer.transform.localRotation;
					clone.transform.localScale = highlightRenderer.transform.localScale;
					var mr = clone.GetComponent<MeshRenderer>();
					if (mr != null)
					{
						var baseMat = mr.sharedMaterial;
						var mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
						if (mat.HasProperty("_Color"))
						{
							// Use serialized color; fallback to default constant to avoid magic numbers
							var color = skillHighlightColor;
							if (color.a <= 0f) color = DefaultSkillHighlightColor;
							mat.color = color;
						}
						mr.sharedMaterial = mat;
						mr.enabled = false;
						skillHighlightRenderer = mr;
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
				// Mutual exclusivity: enabling skill highlight disables move highlight on the same slot
				if (isVisible && highlightRenderer != null)
				{
					highlightRenderer.enabled = false;
				}
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


