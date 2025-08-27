using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ManaGambit
{
	public class UnitStatusIcons : MonoBehaviour
	{
		private sealed class FixedWorldRotation : MonoBehaviour
		{
			public Quaternion targetWorldRotation = Quaternion.identity;
			private void OnEnable()
			{
				transform.rotation = targetWorldRotation;
			}
			private void LateUpdate()
			{
				if (transform.rotation != targetWorldRotation)
				{
					transform.rotation = targetWorldRotation;
				}
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

		private readonly Dictionary<string, GameObject> activeByName = new Dictionary<string, GameObject>();
		private readonly Dictionary<string, Sprite> nameToSprite = new Dictionary<string, Sprite>();

		private void Awake()
		{
			if (container == null) container = transform;
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
			// Ensure world rotation is fixed to identity regardless of parent rotation
			var fixedRot = go.GetComponent<FixedWorldRotation>();
			if (fixedRot == null) fixedRot = go.AddComponent<FixedWorldRotation>();
			fixedRot.targetWorldRotation = Quaternion.identity;
			go.transform.rotation = Quaternion.identity;
			// Try Image first
			bool usedSprite = false;
			var img = go.GetComponentInChildren<Image>(true);
			if (img != null && nameToSprite.TryGetValue(statusName, out var spriteImg))
			{
				img.sprite = spriteImg;
				img.enabled = true;
				usedSprite = true;
			}
			// Try SpriteRenderer next
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
			// Fallback: use TextMeshPro if no sprite found
			if (!usedSprite)
			{
				var tmpUgui = go.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmpUgui != null)
				{
					tmpUgui.text = statusName;
					tmpUgui.enabled = true;
					tmpUgui.gameObject.SetActive(true);
				}
				else
				{
					var tmp = go.GetComponentInChildren<TextMeshPro>(true);
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
			activeByName[statusName] = go;
		}

		private void Hide(string statusName)
		{
			if (!activeByName.TryGetValue(statusName, out var go)) return;
			if (go != null) Destroy(go);
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


