using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System;

namespace ManaGambit
{
	public class SkillBarUI : MonoBehaviour, ISkillBarUI
	{
		private const int SkillSlotCount = 4;
		[SerializeField] private UnitConfig unitConfig;
		[SerializeField] private Button[] slotButtons = new Button[SkillSlotCount];
		[SerializeField] private Image[] slotImages = new Image[SkillSlotCount];
		[SerializeField] private TextMeshProUGUI[] slotTexts = new TextMeshProUGUI[SkillSlotCount];
		[SerializeField] private Image[] slotSelectionOverlays = new Image[SkillSlotCount];
		[SerializeField] private Sprite[] iconLibrary; // Assign all skill sprites here; keyed by sprite.name
		[SerializeField] private Sprite defaultSkillSprite;
		[SerializeField] private bool debugIcons = false;

		private Dictionary<string, Sprite> iconLookup;
		private Dictionary<string, string> canonicalCache;

		[System.ThreadStatic]
		private static System.Text.StringBuilder canonicalStringBuilder;

		private Unit boundUnit;
		private bool isSubscribedToManaEvents = false;
		private ClickInput cachedClickInput;

		private void Awake()
		{
			RebuildIconLookup();
			if (debugIcons)
			{
				int libCount = iconLibrary != null ? iconLibrary.Length : 0;
				Debug.Log($"[SkillBarUI] Awake: iconLibrary count={libCount}");
			}
			AutoWireSlotsIfNeeded();
			ResetSlots();
			
			// Cache ClickInput reference to avoid FindFirstObjectByType calls
			cachedClickInput = FindFirstObjectByType<ClickInput>();
		}

		private void OnValidate()
		{
			RebuildIconLookup();
		}

		private void OnEnable()
		{
			TrySubscribeToManaEvents();
		}

		private void OnDisable()
		{
			UnsubscribeFromManaEvents();
		}

		private void Update()
		{
			// Handle late initialization if ManaBarUI.Instance becomes available after OnEnable
			if (!isSubscribedToManaEvents && ManaBarUI.Instance != null)
			{
				TrySubscribeToManaEvents();
			}
		}

		private void TrySubscribeToManaEvents()
		{
			if (!isSubscribedToManaEvents && ManaBarUI.Instance != null)
			{
				ManaBarUI.Instance.OnManaChanged += HandleManaChanged;
				isSubscribedToManaEvents = true;
			}
		}

		private void UnsubscribeFromManaEvents()
		{
			if (isSubscribedToManaEvents && ManaBarUI.Instance != null)
			{
				ManaBarUI.Instance.OnManaChanged -= HandleManaChanged;
				isSubscribedToManaEvents = false;
			}
		}

		public void BindUnit(Unit unit)
		{
			boundUnit = unit;
			Refresh();
		}

		public void SetSelectedSkillIndex(int index)
		{
			for (int i = 0; i < SkillSlotCount; i++)
			{
				var overlay = (slotSelectionOverlays != null && i < slotSelectionOverlays.Length) ? slotSelectionOverlays[i] : null;
				if (overlay != null) overlay.enabled = (i == index);
			}
		}

		public void Clear()
		{
			boundUnit = null;
			ResetSlots();
		}

		private void Refresh()
		{
			ResetSlots();
			if (boundUnit == null) return;
			var cfg = unitConfig != null ? unitConfig : (NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null);
			if (cfg == null) return;
			// Ensure config is populated
			if ((cfg.units == null || cfg.units.Length == 0))
			{
				try 
				{ 
					cfg.LoadFromJson(); 
				} 
				catch (System.IO.IOException ex)
				{
					Debug.LogError($"[SkillBarUI] Failed to load UnitConfig from JSON due to I/O error: {ex.Message}");
					// Continue with current flow for I/O errors (file not found, access denied, etc.)
				}
				catch (System.ArgumentException ex)
				{
					Debug.LogError($"[SkillBarUI] Failed to load UnitConfig from JSON due to invalid argument: {ex.Message}");
					// Continue with current flow for argument errors (invalid JSON format, etc.)
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"[SkillBarUI] Failed to load UnitConfig from JSON due to unexpected error: {ex.Message}\nStack trace: {ex.StackTrace}");
					// Continue with current flow for other recoverable errors
				}
				if (debugIcons) Debug.Log("[SkillBarUI] UnitConfig was empty; attempted LoadFromJson().");
			}
			var data = cfg.GetData(boundUnit.PieceId);
			if (data == null)
			{
				// Fallback: case-insensitive lookup
				data = FindUnitDataFallback(cfg, boundUnit.PieceId);
				if (debugIcons && data != null) Debug.Log($"[SkillBarUI] Fallback matched UnitData for pieceId='{boundUnit.PieceId}'.");
			}
			if (debugIcons && data == null) Debug.LogWarning($"[SkillBarUI] No UnitConfig data for pieceId='{boundUnit.PieceId}'");
			if (data == null || data.actions == null) return;
			for (int i = 0; i < SkillSlotCount; i++)
			{
				var button = GetButton(i);
				var image = GetImage(i);
				var text = GetText(i);
				if (button == null || image == null)
				{
					if (debugIcons) Debug.LogWarning($"[SkillBarUI] Slot {i}: missing {(button==null?"Button":"")} {(image==null?"Image":"")}");
					continue;
				}

				var action = (i < data.actions.Length) ? data.actions[i] : null;
				if (action == null)
				{
					image.enabled = false;
					if (text != null) { text.text = string.Empty; text.enabled = false; }
					button.interactable = false;
					continue;
				}

				// Assign sprite from action mapping with robust name resolution
				var sprite = ResolveIconForAction(action, i);
				image.sprite = sprite;
				image.enabled = (sprite != null); // if none found, disable the image
				if (text != null)
				{
					text.text = GetActionDisplayName(action, i);
					text.enabled = !string.IsNullOrEmpty(text.text);
				}
				if (debugIcons)
				{
					var actionName = string.IsNullOrEmpty(action.shortDisplayName) ? action.name : action.shortDisplayName;
					Debug.Log($"[SkillBarUI] Slot {i}: action='{actionName}' iconKey='{action.icon}' => sprite='{(sprite!=null?sprite.name:"<none>")}'");
				}

				int capturedIndex = i;
				button.onClick.AddListener(() => OnSkillClicked(capturedIndex));
				button.interactable = IsSkillUsableByPips(action);
			}
		}

		private bool IsSkillUsableByPips(UnitConfig.ActionInfo action)
		{
			if (action == null) return false;
			// Determine mana/pip cost from fields; server supports top-level manaCost or nested attack.manaCost
			int cost = Mathf.Max(0, action.manaCost);
			var bar = ManaBarUI.Instance;
			if (bar == null) return true; // don't block if mana bar missing
			return bar.CurrentPips >= Mathf.Max(0, cost);
		}

		private void HandleManaChanged(int pips)
		{
			if (boundUnit != null)
			{
				Refresh();
			}
		}

		private void ResetSlots()
		{
			for (int i = 0; i < SkillSlotCount; i++)
			{
				var image = GetImage(i);
				if (image != null)
				{
					image.sprite = null;
					image.enabled = false;
				}
				var text = GetText(i);
				if (text != null)
				{
					text.text = string.Empty;
					text.enabled = false;
				}
				var button = GetButton(i);
				if (button != null)
				{
					button.onClick.RemoveAllListeners();
					button.interactable = false;
				}
				var overlay = (slotSelectionOverlays != null && i < slotSelectionOverlays.Length) ? slotSelectionOverlays[i] : null;
				if (overlay != null) overlay.enabled = false;
			}
		}

		private void AutoWireSlotsIfNeeded()
		{
			// If any slot is missing, try to auto-wire from child Buttons
			bool needsButtons = false;
			if (slotButtons == null || slotButtons.Length < SkillSlotCount) needsButtons = true;
			else
			{
				for (int i = 0; i < SkillSlotCount; i++) { if (slotButtons[i] == null) { needsButtons = true; break; } }
			}
			if (needsButtons)
			{
				var found = GetComponentsInChildren<Button>(true);
				if (found != null && found.Length > 0)
				{
					System.Array.Sort(found, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
					var arr = new Button[SkillSlotCount];
					for (int i = 0; i < SkillSlotCount && i < found.Length; i++) arr[i] = found[i];
					slotButtons = arr;
					if (debugIcons) Debug.Log($"[SkillBarUI] Auto-wired {found.Length} Button(s). Using first {SkillSlotCount}.");
				}
				else if (debugIcons)
				{
					Debug.LogWarning("[SkillBarUI] No Buttons found to auto-wire.");
				}
			}

			// If any slot image is missing, default to the Button target graphic image
			bool needsImages = false;
			if (slotImages == null || slotImages.Length < SkillSlotCount) needsImages = true;
			else
			{
				for (int i = 0; i < SkillSlotCount; i++) { if (slotImages[i] == null) { needsImages = true; break; } }
			}
			if (needsImages && slotButtons != null)
			{
				var imgArr = new Image[SkillSlotCount];
				for (int i = 0; i < SkillSlotCount; i++)
				{
					var btn = (i < slotButtons.Length) ? slotButtons[i] : null;
					if (btn != null && btn.image != null) imgArr[i] = btn.image;
					else if (btn != null) imgArr[i] = btn.GetComponentInChildren<Image>(true);
				}
				slotImages = imgArr;
				if (debugIcons) Debug.Log("[SkillBarUI] Auto-wired slot Images from Buttons.");
			}

			// Auto-wire texts if missing
			bool needsTexts = false;
			if (slotTexts == null || slotTexts.Length < SkillSlotCount) needsTexts = true;
			else
			{
				for (int i = 0; i < SkillSlotCount; i++) { if (slotTexts[i] == null) { needsTexts = true; break; } }
			}
			if (needsTexts)
			{
				var foundTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
				if (foundTexts != null && foundTexts.Length > 0)
				{
					System.Array.Sort(foundTexts, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
					var arrT = new TextMeshProUGUI[SkillSlotCount];
					for (int i = 0; i < SkillSlotCount && i < foundTexts.Length; i++) arrT[i] = foundTexts[i];
					slotTexts = arrT;
					if (debugIcons) Debug.Log($"[SkillBarUI] Auto-wired {foundTexts.Length} Text(s). Using first {SkillSlotCount}.");
				}
			}
		}

		private void RebuildIconLookup()
		{
			if (iconLookup == null) iconLookup = new Dictionary<string, Sprite>(32, System.StringComparer.OrdinalIgnoreCase);
			else iconLookup.Clear();
			if (canonicalCache == null) canonicalCache = new Dictionary<string, string>(32, System.StringComparer.OrdinalIgnoreCase);
			else canonicalCache.Clear();
			if (iconLibrary == null || iconLibrary.Length == 0) return;
			for (int i = 0; i < iconLibrary.Length; i++)
			{
				var s = iconLibrary[i];
				if (s == null) continue;
				var key = s.name; // use sprite file name
				if (!string.IsNullOrEmpty(key))
				{
					iconLookup[key] = s;
					var normalized = NormalizeKey(key);
					if (!string.IsNullOrEmpty(normalized)) iconLookup[normalized] = s;
					var canonical = CanonicalKey(key);
					canonicalCache[key] = canonical;
					if (!string.IsNullOrEmpty(canonical)) iconLookup[canonical] = s;
					// Also precompute common prefixed forms to maximize hit rate
					if (!string.IsNullOrEmpty(normalized))
					{
						var prefixedNorm = "skill_" + normalized.TrimStart('_');
						iconLookup[prefixedNorm] = s;
					}
					if (!string.IsNullOrEmpty(canonical))
					{
						var prefixedCanon = "skill" + canonical;
						iconLookup[prefixedCanon] = s;
					}
				}
			}
		}

		private Sprite ResolveIcon(string iconKey)
		{
			if (string.IsNullOrEmpty(iconKey)) return null;
			if (iconLookup != null && iconLookup.TryGetValue(iconKey, out var sprite)) return sprite;
			var normalized = NormalizeKey(iconKey);
			if (iconLookup != null && !string.IsNullOrEmpty(normalized))
			{
				if (iconLookup.TryGetValue(normalized, out var sprite2)) return sprite2;
				// Common prefix fallback: allow JSON keys without the "Skill_" prefix to match sprite names that have it
				var withSkillPrefix = "skill_" + normalized.TrimStart('_');
				if (iconLookup.TryGetValue(withSkillPrefix, out var sprite3)) return sprite3;
			}
			var canonical = CanonicalKey(iconKey);
			if (iconLookup != null && !string.IsNullOrEmpty(canonical))
			{
				if (iconLookup.TryGetValue(canonical, out var sprite4)) return sprite4;
				var withSkillCanon = "skill" + canonical;
				if (iconLookup.TryGetValue(withSkillCanon, out var sprite5)) return sprite5;
			}
			// Last resort: fuzzy match on canonical forms (substring either direction)
			if (iconLookup != null && iconLookup.Count > 0 && !string.IsNullOrEmpty(canonical))
			{
				Sprite fuzzy = null;
				foreach (var kv in iconLookup)
				{
					// Use cached canonical form if available, otherwise compute it
					if (!canonicalCache.TryGetValue(kv.Key, out var kcanon))
					{
						kcanon = CanonicalKey(kv.Key);
						canonicalCache[kv.Key] = kcanon;
					}
					if (string.IsNullOrEmpty(kcanon)) continue;
					if (kcanon == canonical || kcanon.Contains(canonical) || canonical.Contains(kcanon))
					{
						fuzzy = kv.Value;
						break;
					}
				}
				if (fuzzy != null) return fuzzy;
			}
			return null;
		}

		private Sprite ResolveIconForAction(UnitConfig.ActionInfo action, int actionIndex)
		{
			if (action == null) return null;

			// Priority 1: explicit icon key
			var s = ResolveIcon(action.icon);
			if (s != null) return s;

			// Priority 2: action name if explicit icon key not found or not provided
			if (!string.IsNullOrEmpty(action.name))
			{
				s = ResolveIcon(action.name);
				if (s != null) return s;
			}

			// No suitable icon found
			return defaultSkillSprite;
		}

		private static string NormalizeKey(string key)
		{
			if (string.IsNullOrEmpty(key)) return null;
			var k = key.Trim().ToLowerInvariant();
			k = k.Replace(' ', '_').Replace('-', '_').Replace('/', '_').Replace('\\', '_').Replace('.', '_');
			return k;
		}

		private static string CanonicalKey(string key)
		{
			if (string.IsNullOrEmpty(key)) return null;
			var lower = key.Trim().ToLowerInvariant();
			
			// Ensure thread-static StringBuilder is allocated
			if (canonicalStringBuilder == null)
			{
				canonicalStringBuilder = new System.Text.StringBuilder(lower.Length);
			}
			else
			{
				canonicalStringBuilder.Clear();
				canonicalStringBuilder.EnsureCapacity(lower.Length);
			}
			
			for (int i = 0; i < lower.Length; i++)
			{
				char c = lower[i];
				if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
				{
					canonicalStringBuilder.Append(c);
				}
			}
			return canonicalStringBuilder.ToString();
		}

		private UnitConfig.UnitData FindUnitDataFallback(UnitConfig cfg, string pieceId)
		{
			if (cfg == null || cfg.units == null || cfg.units.Length == 0) return null;
			if (string.IsNullOrEmpty(pieceId)) return null;
			for (int i = 0; i < cfg.units.Length; i++)
			{
				var u = cfg.units[i];
				if (u == null || string.IsNullOrEmpty(u.pieceId)) continue;
				if (string.Equals(u.pieceId, pieceId, System.StringComparison.OrdinalIgnoreCase)) return u;
			}
			return null;
		}

		private Button GetButton(int index)
		{
			return (slotButtons != null && index >= 0 && index < slotButtons.Length) ? slotButtons[index] : null;
		}

		private Image GetImage(int index)
		{
			return (slotImages != null && index >= 0 && index < slotImages.Length) ? slotImages[index] : null;
		}

		private TextMeshProUGUI GetText(int index)
		{
			return (slotTexts != null && index >= 0 && index < slotTexts.Length) ? slotTexts[index] : null;
		}

		private void OnSkillClicked(int actionIndex)
		{
			if (boundUnit == null) return;
			if (cachedClickInput != null && cachedClickInput.BeginSkillTargeting(boundUnit, actionIndex)) return;
			// Fallback: send immediately if input targeting not available
			if (IntentManager.Instance != null && !string.IsNullOrEmpty(boundUnit.UnitID))
			{
				var target = new SkillTarget();
				_ = IntentManager.Instance.SendUseSkillIntent(boundUnit.UnitID, actionIndex, target);
			}
		}

		private static string GetActionDisplayName(UnitConfig.ActionInfo action, int index)
		{
			if (action == null) return string.Empty;
			int cost = Mathf.Max(0, action.manaCost);
			string baseName;
			if (!string.IsNullOrEmpty(action.shortDisplayName)) baseName = action.shortDisplayName;
			else if (!string.IsNullOrEmpty(action.name)) baseName = action.name;
			else if (!string.IsNullOrEmpty(action.type))
			{
				if (action.type == "basic") baseName = "Basic";
				else if (action.type == "ult") baseName = "Ult";
				else if (action.type == "skill") baseName = $"Skill {index + 1}";
				else baseName = action.type;
			}
			else baseName = $"Skill {index + 1}";
			return $"{baseName} ({cost})";
		}

		// ( Mana display moved to ManaBarUI )
	}
}


