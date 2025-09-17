using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ManaGambit
{
	public class UnitStatusIcons : MonoBehaviour
	{
		private sealed class BillboardLimited : MonoBehaviour
		{
			private void LateUpdate()
			{
				var cam = Camera.main;
				if (cam == null) return;
				
				// Get world positions
				Vector3 worldPos = transform.position;
				Vector3 cameraPos = cam.transform.position;
				
				// Calculate full direction from billboard to camera (including Y component for full billboarding)
				Vector3 directionToCamera = (cameraPos - worldPos).normalized;
				
				// Make the transform look at the camera position
				transform.LookAt(cameraPos);
			}
		}

		[System.Serializable]
		public class SpriteMapping
		{
			public string name;
			public Sprite sprite;
		}

		[SerializeField] private Transform container; // Optional; defaults to this.transform
		[SerializeField] private GameObject iconTemplate; // Prefab with Image or SpriteRenderer (disabled by default)
		[SerializeField] private SpriteMapping[] sprites;
		[SerializeField] private float iconSpacing = 0.25f;
		[SerializeField, Tooltip("When enabled, shows only text (no icons), using TextMeshProUGUI on the template.")]
		private bool textOnly = false;
		[SerializeField, Tooltip("HP slider for player-owned units (green)")]
		private Slider greenHpSlider;
		[SerializeField, Tooltip("HP slider for enemy units (red)")]
		private Slider redHpSlider;
		[SerializeField, Tooltip("Diamond indicator for player-owned units (green)")]
		private GameObject greenDiamond;
		[SerializeField, Tooltip("Diamond indicator for enemy units (red)")]
		private GameObject redDiamond;

		private readonly Dictionary<string, GameObject> activeByName = new Dictionary<string, GameObject>();
		private readonly Dictionary<string, Sprite> nameToSprite = new Dictionary<string, Sprite>();

		private void Awake()
		{
			if (container == null)
			{
				// Try find a child Canvas to use as container
				var childCanvas = GetComponentInChildren<Canvas>(true);
				if (childCanvas != null)
				{
					container = childCanvas.transform;
					// If world-space canvas, assign the main camera
					if (childCanvas.renderMode == RenderMode.WorldSpace)
					{
						var cam = Camera.main;
						if (cam != null)
						{
							childCanvas.worldCamera = cam;
						}
					}
				}
				if (container == null) container = transform;
			}
			
			// Add billboard behavior to the container instead of individual icons
			if (container != null)
			{
				var billboard = container.GetComponent<BillboardLimited>();
				if (billboard == null) 
				{
					billboard = container.gameObject.AddComponent<BillboardLimited>();
				}
			}
			
			AutoWireHpSlidersIfNeeded();
			if (sprites != null)
			{
				for (int i = 0; i < sprites.Length; i++)
				{
					var s = sprites[i];
					if (s != null && !string.IsNullOrEmpty(s.name) && s.sprite != null)
					{
						nameToSprite[s.name] = s.sprite;
					}
				}
			}
		}

		private void AutoWireHpSlidersIfNeeded()
		{
			if (greenHpSlider != null && redHpSlider != null) return;
			var sliders = container != null ? container.GetComponentsInChildren<Slider>(true) : GetComponentsInChildren<Slider>(true);
			if (sliders == null || sliders.Length == 0) return;
			for (int i = 0; i < sliders.Length; i++)
			{
				var s = sliders[i]; if (s == null) continue;
				var n = s.gameObject.name.ToLowerInvariant();
				if (greenHpSlider == null && (n.Contains("green") || n.Contains("ally") || n.Contains("own"))) { greenHpSlider = s; continue; }
				if (redHpSlider == null && (n.Contains("red") || n.Contains("enemy") || n.Contains("foe"))) { redHpSlider = s; continue; }			}
			// Fallback: first two sliders by order
			if (greenHpSlider == null && sliders.Length > 0) greenHpSlider = sliders[0];
			if (redHpSlider == null && sliders.Length > 1) redHpSlider = sliders[1];
		}

		public void SetHp(int current, int max, bool isOwn)
		{
			AutoWireHpSlidersIfNeeded();
			var active = isOwn ? greenHpSlider : redHpSlider;
			var inactive = isOwn ? redHpSlider : greenHpSlider;
			if (inactive != null) inactive.gameObject.SetActive(false);
			if (active == null) return;
			active.wholeNumbers = true;
			active.minValue = 0;
			active.maxValue = Mathf.Max(1, max);
			active.value = Mathf.Clamp(current, 0, max);
			// Show only after damage and while hp > 0
			bool shouldShow = (current < max) && (current > 0);
			active.gameObject.SetActive(shouldShow);
			
			// Hide the visible diamond when HP bar is shown
			if (shouldShow)
			{
				HideAllDiamonds();
			}
			else
			{
				// Show appropriate diamond when HP bar is not visible
				SetDiamondVisibility(isOwn);
			}
		}

		public void SetDiamondVisibility(bool isPlayerUnit)
		{
			// Show green diamond for player units, red for enemy units
			// Only one should be visible at a time
			if (greenDiamond != null) greenDiamond.SetActive(isPlayerUnit);
			if (redDiamond != null) redDiamond.SetActive(!isPlayerUnit);
		}

		private void HideAllDiamonds()
		{
			if (greenDiamond != null) greenDiamond.SetActive(false);
			if (redDiamond != null) redDiamond.SetActive(false);
		}

		public void Apply(StatusChange[] changes)
		{
			if (changes == null) return;
			for (int i = 0; i < changes.Length; i++)
			{
				var c = changes[i];
				if (c == null || string.IsNullOrEmpty(c.name)) continue;
				switch ((c.op ?? string.Empty).Trim())
				{
					case "Add":
						Show(c.name);
						break;
					case "Remove":
						Hide(c.name);
						break;
					default:
						// Unknown op — default to Add to make it visible
						Show(c.name);
						break;
				}
			}
			RepositionIcons();
		}

		public void HideKey(string statusName)
		{
			if (string.IsNullOrEmpty(statusName)) return;
			Hide(statusName);
			RepositionIcons();
		}

		public void ShowTextKey(string statusName)
		{
			if (string.IsNullOrEmpty(statusName)) return;
			Show(statusName);
			RepositionIcons();
		}

		private void Show(string statusName)
		{
			if (activeByName.TryGetValue(statusName, out var existing) && existing != null)
			{
				ConfigureAsText(existing, statusName);
				existing.SetActive(true);
				return;
			}
			if (iconTemplate == null || container == null)
			{
				Debug.LogWarning($"[UnitStatusIcons] Missing iconTemplate or container for {name} (statusName={statusName})");
				return;
			}
			var go = Instantiate(iconTemplate, container);
			go.name = $"StatusIcon_{statusName}";
			// For now, always show as text
			ConfigureAsText(go, statusName);
			go.SetActive(true);
			activeByName[statusName] = go;
		}

		private void Hide(string statusName)
		{
			if (!activeByName.TryGetValue(statusName, out var go)) return;
			if (go != null)
			{
				// Disable text and sprite visuals, then deactivate for reuse
				var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui != null) tmpUgui.enabled = false;
				var tmp = go.GetComponentInChildren<TextMeshPro>(true);
				if (tmp != null) tmp.enabled = false;
				var imgAll = go.GetComponentsInChildren<Image>(true);
				for (int ii = 0; ii < imgAll.Length; ii++) { if (imgAll[ii] != null) imgAll[ii].enabled = false; }
				var srAll = go.GetComponentsInChildren<SpriteRenderer>(true);
				for (int ii = 0; ii < srAll.Length; ii++) { if (srAll[ii] != null) srAll[ii].enabled = false; }
				go.SetActive(false);
			}
		}

		private void ConfigureAsText(GameObject go, string label)
		{
			// Disable sprite visuals for now
			var imgAll = go.GetComponentsInChildren<Image>(true);
			for (int ii = 0; ii < imgAll.Length; ii++) { if (imgAll[ii] != null) imgAll[ii].enabled = false; }
			var srAll = go.GetComponentsInChildren<SpriteRenderer>(true);
			for (int ii = 0; ii < srAll.Length; ii++) { if (srAll[ii] != null) srAll[ii].enabled = false; }

			// Prefer TMP UGUI; fallback to 3D TMP
			var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
			if (tmpUgui == null)
			{
				// No text component on icon - create a unique one
				TextMeshProUGUI templateUgui = null;
				if (container != null) templateUgui = container.GetComponentInChildren<TextMeshProUGUI>(true);
				
				if (templateUgui != null)
				{
					// Clone the template text component to create a unique instance
					var newTextGo = new GameObject("TextMeshPro UGUI");
					newTextGo.transform.SetParent(go.transform, false);
					tmpUgui = newTextGo.AddComponent<TextMeshProUGUI>();
					
					// Copy settings from template
					tmpUgui.font = templateUgui.font;
					tmpUgui.fontSize = templateUgui.fontSize;
					tmpUgui.color = templateUgui.color;
					tmpUgui.alignment = templateUgui.alignment;
					tmpUgui.fontStyle = templateUgui.fontStyle;
					tmpUgui.textWrappingMode = templateUgui.textWrappingMode;
					
					// Copy RectTransform settings if available
					if (templateUgui.rectTransform != null && tmpUgui.rectTransform != null)
					{
						var templateRect = templateUgui.rectTransform;
						var newRect = tmpUgui.rectTransform;
						newRect.anchorMin = templateRect.anchorMin;
						newRect.anchorMax = templateRect.anchorMax;
						newRect.anchoredPosition = templateRect.anchoredPosition;
						newRect.sizeDelta = templateRect.sizeDelta;
						newRect.localScale = templateRect.localScale;
					}
				}
				else
				{
					// No template found - create a basic text component
					var newTextGo = new GameObject("TextMeshPro UGUI");
					newTextGo.transform.SetParent(go.transform, false);
					tmpUgui = newTextGo.AddComponent<TextMeshProUGUI>();
					tmpUgui.fontSize = 14f;
					tmpUgui.color = Color.white;
					tmpUgui.alignment = TextAlignmentOptions.Center;
				}
			}
			
			if (tmpUgui != null)
			{
				tmpUgui.text = label;
				tmpUgui.enabled = true;
				tmpUgui.gameObject.SetActive(true);
				return;
			}
			
			var tmp = go.GetComponentInChildren<TextMeshPro>(true);
			if (tmp == null)
			{
				// No text component on icon - create a unique one
				TextMeshPro templateTmp = null;
				if (container != null) templateTmp = container.GetComponentInChildren<TextMeshPro>(true);
				
				if (templateTmp != null)
				{
					// Clone the template text component to create a unique instance
					var newTextGo = new GameObject("TextMeshPro");
					newTextGo.transform.SetParent(go.transform, false);
					tmp = newTextGo.AddComponent<TextMeshPro>();
					
					// Copy settings from template
					tmp.font = templateTmp.font;
					tmp.fontSize = templateTmp.fontSize;
					tmp.color = templateTmp.color;
					tmp.alignment = templateTmp.alignment;
					tmp.fontStyle = templateTmp.fontStyle;
					tmp.textWrappingMode = templateTmp.textWrappingMode;
					
					// Copy transform settings
					if (templateTmp.transform != null)
					{
						var templateTransform = templateTmp.transform;
						var newTransform = tmp.transform;
						newTransform.localPosition = templateTransform.localPosition;
						newTransform.localRotation = templateTransform.localRotation;
						newTransform.localScale = templateTransform.localScale;
					}
				}
				else
				{
					// No template found - create a basic text component
					var newTextGo = new GameObject("TextMeshPro");
					newTextGo.transform.SetParent(go.transform, false);
					tmp = newTextGo.AddComponent<TextMeshPro>();
					tmp.fontSize = 2f;
					tmp.color = Color.white;
					tmp.alignment = TextAlignmentOptions.Center;
				}
			}
			
			if (tmp != null)
			{
				tmp.text = label;
				tmp.enabled = true;
				tmp.gameObject.SetActive(true);
			}
			else
			{
				Debug.LogWarning($"[UnitStatusIcons] Failed to create TextMeshPro component for label '{label}' on template {iconTemplate?.name}");
			}
		}

		private void RepositionIcons()
		{
			int idx = 0;
			foreach (var kv in activeByName)
			{
				if (kv.Value == null || !kv.Value.activeSelf) continue;
				var t = kv.Value.transform as RectTransform;
				if (t != null)
				{
					t.anchoredPosition = new Vector2(idx * iconSpacing * 100f, 0f);
				}
				else
				{
					kv.Value.transform.localPosition = new Vector3(idx * iconSpacing, 0f, 0f);
				}
				idx += 1;
			}
		}
	}
}


