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
				// Face camera along Y (0 or 180) and lock Z; do not lock X
				float y = cam.transform.position.z >= transform.position.z ? 0f : 180f;
				var e = transform.rotation.eulerAngles;
				var rot = Quaternion.Euler(e.x, y, 0f);
				transform.rotation = rot;
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
				if (greenHpSlider == null && (n.Contains("green", System.StringComparison.OrdinalIgnoreCase) || n.Contains("ally", System.StringComparison.OrdinalIgnoreCase) || n.Contains("own", System.StringComparison.OrdinalIgnoreCase))) { greenHpSlider = s; continue; }
				if (redHpSlider == null && (n.Contains("red", System.StringComparison.OrdinalIgnoreCase) || n.Contains("enemy", System.StringComparison.OrdinalIgnoreCase) || n.Contains("foe", System.StringComparison.OrdinalIgnoreCase))) { redHpSlider = s; continue; }
			}
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
			if (activeByName.ContainsKey(statusName)) return;
			if (iconTemplate == null || container == null)
			{
				Debug.LogWarning($"[UnitStatusIcons] Missing iconTemplate or container for {name} (statusName={statusName})");
				return;
			}
			var go = Instantiate(iconTemplate, container);
			go.name = $"StatusIcon_{statusName}";
			go.SetActive(true);
			// Billboard behavior
			var billboard = go.GetComponent<BillboardLimited>();
			if (billboard == null) billboard = go.AddComponent<BillboardLimited>();
			// Choose display mode
			if (textOnly)
			{
				// Disable any sprite components
				var imgAll = go.GetComponentsInChildren<Image>(true);
				for (int ii = 0; ii < imgAll.Length; ii++) { if (imgAll[ii] != null) imgAll[ii].enabled = false; }
				var srAll = go.GetComponentsInChildren<SpriteRenderer>(true);
				for (int ii = 0; ii < srAll.Length; ii++) { if (srAll[ii] != null) srAll[ii].enabled = false; }
				// Use TMP text and set to sprite name if available; fall back to status key
				string label = statusName;
				if (nameToSprite.TryGetValue(statusName, out var mappedSprite) && mappedSprite != null)
				{
					label = string.IsNullOrEmpty(mappedSprite.name) ? statusName : mappedSprite.name;
				}
				var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui == null && container != null) tmpUgui = container.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui != null)
				{
					tmpUgui.text = label;
					tmpUgui.enabled = true;
					tmpUgui.gameObject.SetActive(true);
				}
				else
				{
					var tmp = go.GetComponentInChildren<TextMeshPro>(true);
					if (tmp == null && container != null) tmp = container.GetComponentInChildren<TextMeshPro>(true);
					if (tmp != null)
					{
						tmp.text = label;
						tmp.enabled = true;
						tmp.gameObject.SetActive(true);
					}
				}
			}
			else
			{
				// Icon-first mode (previous behavior)
				bool usedSprite = false;
				var img = go.GetComponentInChildren<Image>(true);
				if (img != null && nameToSprite.TryGetValue(statusName, out var spriteImg))
				{
					img.sprite = spriteImg;
					img.enabled = true;
					usedSprite = true;
				}
				if (!usedSprite)
				{
					var sr = go.GetComponentInChildren<SpriteRenderer>(true);
					if (sr != null && nameToSprite.TryGetValue(statusName, out var spriteSr))
					{
						sr.sprite = spriteSr;
						sr.enabled = true;
						usedSprite = true;
					}
				}
				if (!usedSprite)
				{
					var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
					if (tmpUgui == null && container != null) tmpUgui = container.GetComponentInChildren<TextMeshProUGUI>(true);
					if (tmpUgui != null)
					{
						tmpUgui.text = statusName;
						tmpUgui.enabled = true;
						tmpUgui.gameObject.SetActive(true);
					}
					else
					{
						var tmp = go.GetComponentInChildren<TextMeshPro>(true);
						if (tmp == null && container != null) tmp = container.GetComponentInChildren<TextMeshPro>(true);
						if (tmp != null)
						{
							tmp.text = statusName;
							tmp.enabled = true;
							tmp.gameObject.SetActive(true);
						}
						else
						{
							Debug.LogWarning($"[UnitStatusIcons] No sprite or TextMeshPro found for {statusName} on template {iconTemplate.name}");
						}
					}
				}
			}
			activeByName[statusName] = go;
		}

		private void Hide(string statusName)
		{
			if (!activeByName.TryGetValue(statusName, out var go)) return;
			if (go != null)
			{
				// Ensure text is disabled when hidden (for text-only mode)
				var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui == null && container != null) tmpUgui = container.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui != null) tmpUgui.enabled = false;
				var tmp = go.GetComponentInChildren<TextMeshPro>(true);
				if (tmp == null && container != null) tmp = container.GetComponentInChildren<TextMeshPro>(true);
				if (tmp != null) tmp.enabled = false;
				Destroy(go);
			}
			activeByName.Remove(statusName);
		}

		private void RepositionIcons()
		{
			int idx = 0;
			foreach (var kv in activeByName)
			{
				if (kv.Value == null) continue;
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


