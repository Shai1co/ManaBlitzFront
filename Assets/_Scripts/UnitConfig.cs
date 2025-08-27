
using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Newtonsoft.Json.Linq;

namespace ManaGambit
{
	[CreateAssetMenu(fileName = "UnitConfig", menuName = "ManaGambit/UnitConfig")]
	public class UnitConfig : ScriptableObject
	{
		[SerializeField] private TextAsset jsonAsset;  // Assign characters.json here
		private const string JsonRootDataKey = "data";
		private const int DefaultStatValue = 0; // Avoid magic numbers for defaulting missing json fields

		[System.Serializable]
		public class ActionInfo
		{
			public string name;
			public string shortDisplayName;
			public string type; // basic | skill | ult
			public string icon;
			public int windUpMs; // parsed from action.attack.windUp or action.windUp
			public int cooldownMs; // parsed from action.cooldown or action.attack.cooldown
			public int manaCost; // parsed from action.manaCost or action.attack.manaCost
			// Effect objects (subset for UI/targeting needs)
			public AttackEffect attack;
			public MoveEffect move;
			public SwapEffect swap;
			public BuffEffect buff;
			public AuraEffect aura;
			public MultiHitEffect multiHit;
		}

		[System.Serializable]
		public class MovePattern
		{
			public string dir; // orthogonal | diagonal | forward | any
			public int rangeMin;
			public int rangeMax;
			public bool leap;
			public bool stopOnHit;
			public bool passThrough;
		}

		[System.Serializable]
		public class MoveEffect
		{
			public int manaCost;
			public List<MovePattern> patterns;
			public bool overFriendly;
			public bool overEnemy;
			public PostImpact postImpact;
			public bool targetFriendly;
		}

		[System.Serializable]
		public class PostImpact
		{
			public string behavior; // ReturnHome | LandNearTarget
			public int radius;
			public bool random;
		}

		[System.Serializable]
		public class AttackEffect
		{
			public int damage;
			public string damageType; // physical | magic
			public int speed;
			public List<MovePattern> patterns;
			public bool overFriendly;
			public bool overEnemy;
			public bool stopOnHit;
			public int maxTargets;
			public int pierce;
			public int knockback;
			public AoePattern aoePattern;
			public AoeDamage aoeDamage;
			public DotEffect dot;
			public DebuffEffect debuff;
			public int amount;
			public int interval;
			public int duration;
			public bool requireLos;
		}

		[System.Serializable]
		public class AoePattern { public string dir; public int range; }

		[System.Serializable]
		public class AoeDamage { public int center; public int adjacent; }

		[System.Serializable]
		public class DotEffect { public int damage; public int interval; public int duration; }

		[System.Serializable]
		public class DebuffEffect { public float speedReduction; public int duration; }

		[System.Serializable]
		public class SwapEffect
		{
			public int manaCost;
			public int windUp;
			public int cooldown;
			public List<MovePattern> patterns;
			public bool overFriendly;
			public bool overEnemy;
			public bool targetFriendly;
		}

		[System.Serializable]
		public class BuffEffect
		{
			public int duration;
			public BuffStats effect;
			public string targets; // self | team
		}

		[System.Serializable]
		public class BuffStats
		{
			public int rangeBonus;
			public int gcdReduction;
			public int shotCount;
			public float moveSpeedBonus;
			public int manaCostReduction;
			public int pierce;
		}

		[System.Serializable]
		public class AuraEffect
		{
			public int duration;
			public int radius;
			public AuraStats effect;
			public string targets; // allies | enemies | team
		}

		[System.Serializable]
		public class AuraStats
		{
			public int armor;
		}

		[System.Serializable]
		public class MultiHitEffect
		{
			public int count;
			public int damage;
			public int interval;
			public int iframes;
		}

		[System.Serializable]
		public class UnitData
		{
			public string pieceId;
			public GameObject prefab;  // Manual assignment
			public int hp;
			public int armor;
			public int magicArmor;
			public float critical;
			public float dodge;
			public float hpRegenPerSecond;
			public float moveSpeed;
			public int priority;
			public MoveEffect move; // default movement config
			public ActionInfo[] actions;
		}
		public bool loadOnEnable = true;

		public UnitData[] units;  // Populated from JSON + manual prefabs

		private void OnEnable() { /* preserve prefab assignments; do not auto-populate at runtime */ }

		public GameObject GetPrefab(string pieceId)
		{
			if (units == null || units.Length == 0)
			{
				Debug.LogWarning("UnitConfig: 'units' is null or empty. Make sure to assign a JSON asset and run LoadFromJson().");
				return null;
			}

			foreach (var data in units)
			{
				if (data.pieceId == pieceId) return data.prefab;
			}
			Debug.LogWarning($"No prefab for {pieceId}");
			return null;
		}

		public UnitData GetData(string pieceId)
		{
			if (units == null || units.Length == 0)
			{
				Debug.LogWarning("UnitConfig: 'units' is null or empty. Make sure to assign a JSON asset and run LoadFromJson().");
				return null;
			}

			foreach (var data in units)
			{
				if (data.pieceId == pieceId) return data;
			}
			return null;
		}
		[Button]
		public void LoadFromJson()
		{
			if (jsonAsset == null)
			{
				Debug.LogError("UnitConfig.LoadFromJson: No JSON asset assigned.");
				units = System.Array.Empty<UnitData>();
				return;
			}

			if (string.IsNullOrWhiteSpace(jsonAsset.text))
			{
				Debug.LogError("UnitConfig.LoadFromJson: Assigned JSON asset is empty.");
				units = System.Array.Empty<UnitData>();
				return;
			}

			try
			{
				// Preserve existing prefab assignments by pieceId
				var existing = new Dictionary<string, GameObject>();
				if (units != null)
				{
					for (int i = 0; i < units.Length; i++)
					{
						if (units[i] != null && !string.IsNullOrEmpty(units[i].pieceId) && units[i].prefab != null)
						{
							existing[units[i].pieceId] = units[i].prefab;
						}
					}
				}

				// Support either { "data": { "pieceId": {...} } } or { "pieceId": {...} } shapes
				var rootObject = JObject.Parse(jsonAsset.text);
				var dataToken = rootObject[JsonRootDataKey] ?? rootObject;

				if (dataToken == null || dataToken.Type != JTokenType.Object)
				{
					Debug.LogError("UnitConfig.LoadFromJson: JSON does not contain a valid object at root or 'data'.");
					units = System.Array.Empty<UnitData>();
					return;
				}

				var dataObject = (JObject)dataToken;
				var tempUnits = new List<UnitData>(dataObject.Count);

				foreach (var property in dataObject.Properties())
				{
					var pieceKey = property.Name;
					var valueObj = property.Value as JObject;
					if (valueObj == null)
					{
						Debug.LogWarning($"UnitConfig.LoadFromJson: Skipping '{pieceKey}' because value is not an object.");
						continue;
					}

					var unitData = new UnitData
					{
						pieceId = pieceKey,
						hp = valueObj["hp"]?.Value<int?>() ?? DefaultStatValue,
						armor = valueObj["armor"]?.Value<int?>() ?? DefaultStatValue,
						magicArmor = valueObj["magicArmor"]?.Value<int?>() ?? DefaultStatValue,
						critical = valueObj["critical"]?.Value<float?>() ?? 0f,
						dodge = valueObj["dodge"]?.Value<float?>() ?? 0f,
						hpRegenPerSecond = valueObj["hpRegenPerSecond"]?.Value<float?>() ?? 0f,
						moveSpeed = valueObj["moveSpeed"]?.Value<float?>() ?? 0f,
						priority = valueObj["priority"]?.Value<int?>() ?? 0,
						move = ParseMoveEffect(valueObj["move"] as JObject),
						actions = ParseActions(valueObj["actions"] as JArray)
					};

					if (existing.TryGetValue(pieceKey, out var prefabRef))
					{
						unitData.prefab = prefabRef;
					}

					tempUnits.Add(unitData);
				}

				units = tempUnits.ToArray();
				Debug.Log($"UnitConfig.LoadFromJson: Loaded {units.Length} units (prefabs preserved where available).");
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"UnitConfig.LoadFromJson: Failed to parse JSON. Exception: {ex.Message}");
				units = System.Array.Empty<UnitData>();
			}
		}

		private static ActionInfo[] ParseActions(JArray actions)
		{
			if (actions == null || actions.Type != JTokenType.Array) return System.Array.Empty<ActionInfo>();
			var list = new List<ActionInfo>(actions.Count);
			for (int i = 0; i < actions.Count; i++)
			{
				var obj = actions[i] as JObject;
				if (obj == null) continue;
				var info = new ActionInfo
				{
					name = obj["name"]?.Value<string>(),
					shortDisplayName = obj["shortDisplayName"]?.Value<string>(),
					type = obj["type"]?.Value<string>(),
					icon = obj["icon"]?.Value<string>(),
					windUpMs = ExtractWindUpMs(obj),
					cooldownMs = ExtractCooldownMs(obj),
					manaCost = ExtractManaCost(obj),
					attack = ParseAttackEffect(obj["attack"] as JObject),
					move = ParseMoveEffect(obj["move"] as JObject),
					swap = ParseSwapEffect(obj["swap"] as JObject),
					buff = ParseBuffEffect(obj["buff"] as JObject),
					aura = ParseAuraEffect(obj["aura"] as JObject),
					multiHit = ParseMultiHitEffect(obj["multiHit"] as JObject)
				};
				list.Add(info);
			}
			return list.ToArray();
		}

		private static MoveEffect ParseMoveEffect(JObject obj)
		{
			if (obj == null) return null;
			var effect = new MoveEffect();
			effect.manaCost = obj["manaCost"]?.Value<int?>() ?? 0;
			effect.patterns = ParsePatterns(obj["patterns"] as JArray);
			effect.overFriendly = obj["overFriendly"]?.Value<bool?>() ?? false;
			effect.overEnemy = obj["overEnemy"]?.Value<bool?>() ?? false;
			var post = obj["postImpact"] as JObject;
			if (post != null)
			{
				effect.postImpact = new PostImpact
				{
					behavior = post["behavior"]?.Value<string>(),
					radius = post["radius"]?.Value<int?>() ?? 0,
					random = post["random"]?.Value<bool?>() ?? false
				};
			}
			effect.targetFriendly = obj["targetFriendly"]?.Value<bool?>() ?? false;
			return effect;
		}

		private static List<MovePattern> ParsePatterns(JArray arr)
		{
			var list = new List<MovePattern>();
			if (arr == null || arr.Type != JTokenType.Array) return list;
			for (int i = 0; i < arr.Count; i++)
			{
				var p = arr[i] as JObject;
				if (p == null) continue;
				var range = p["range"] as JArray;
				int min = 0, max = 0;
				if (range != null && range.Count >= 2)
				{
					min = range[0]?.Value<int?>() ?? 0;
					max = range[1]?.Value<int?>() ?? min;
				}
				list.Add(new MovePattern
				{
					dir = p["dir"]?.Value<string>(),
					rangeMin = min,
					rangeMax = max,
					leap = p["leap"]?.Value<bool?>() ?? false,
					stopOnHit = p["stopOnHit"]?.Value<bool?>() ?? false,
					passThrough = p["passThrough"]?.Value<bool?>() ?? false
				});
			}
			return list;
		}

		private static AttackEffect ParseAttackEffect(JObject obj)
		{
			if (obj == null) return null;
			var e = new AttackEffect();
			e.damage = obj["damage"]?.Value<int?>() ?? 0;
			e.speed = obj["speed"]?.Value<int?>() ?? 0;
			e.damageType = obj["type"]?.Value<string>();
			e.patterns = ParsePatterns(obj["patterns"] as JArray);
			e.overFriendly = obj["overFriendly"]?.Value<bool?>() ?? false;
			e.overEnemy = obj["overEnemy"]?.Value<bool?>() ?? false;
			e.stopOnHit = obj["stopOnHit"]?.Value<bool?>() ?? false;
			e.maxTargets = obj["maxTargets"]?.Value<int?>() ?? 0;
			e.pierce = obj["pierce"]?.Value<int?>() ?? 0;
			e.knockback = obj["knockback"]?.Value<int?>() ?? 0;
			var aoeP = obj["aoePattern"] as JObject;
			if (aoeP != null) e.aoePattern = new AoePattern { dir = aoeP["dir"]?.Value<string>(), range = aoeP["range"]?.Value<int?>() ?? 0 };
			var aoeD = obj["aoeDamage"] as JObject;
			if (aoeD != null) e.aoeDamage = new AoeDamage { center = aoeD["center"]?.Value<int?>() ?? 0, adjacent = aoeD["adjacent"]?.Value<int?>() ?? 0 };
			var dot = obj["dot"] as JObject;
			if (dot != null) e.dot = new DotEffect { damage = dot["damage"]?.Value<int?>() ?? 0, interval = dot["interval"]?.Value<int?>() ?? 0, duration = dot["duration"]?.Value<int?>() ?? 0 };
			var debuff = obj["debuff"] as JObject;
			if (debuff != null) e.debuff = new DebuffEffect { speedReduction = debuff["speedReduction"]?.Value<float?>() ?? 0f, duration = debuff["duration"]?.Value<int?>() ?? 0 };
			e.amount = obj["amount"]?.Value<int?>() ?? 0;
			e.interval = obj["interval"]?.Value<int?>() ?? 0;
			e.duration = obj["duration"]?.Value<int?>() ?? 0;
			e.requireLos = obj["requireLos"]?.Value<bool?>() ?? false;
			return e;
		}

		private static SwapEffect ParseSwapEffect(JObject obj)
		{
			if (obj == null) return null;
			return new SwapEffect
			{
				manaCost = obj["manaCost"]?.Value<int?>() ?? 0,
				windUp = obj["windUp"]?.Value<int?>() ?? 0,
				cooldown = obj["cooldown"]?.Value<int?>() ?? 0,
				patterns = ParsePatterns(obj["patterns"] as JArray),
				overFriendly = obj["overFriendly"]?.Value<bool?>() ?? false,
				overEnemy = obj["overEnemy"]?.Value<bool?>() ?? false,
				targetFriendly = obj["targetFriendly"]?.Value<bool?>() ?? false
			};
		}

		private static BuffEffect ParseBuffEffect(JObject obj)
		{
			if (obj == null) return null;
			var e = new BuffEffect();
			e.duration = obj["duration"]?.Value<int?>() ?? 0;
			var eff = obj["effect"] as JObject;
			if (eff != null)
			{
				e.effect = new BuffStats
				{
					rangeBonus = eff["rangeBonus"]?.Value<int?>() ?? 0,
					gcdReduction = eff["gcdReduction"]?.Value<int?>() ?? 0,
					shotCount = eff["shotCount"]?.Value<int?>() ?? 0,
					moveSpeedBonus = eff["moveSpeedBonus"]?.Value<float?>() ?? 0f,
					manaCostReduction = eff["manaCostReduction"]?.Value<int?>() ?? 0,
					pierce = eff["pierce"]?.Value<int?>() ?? 0
				};
			}
			e.targets = obj["targets"]?.Value<string>();
			return e;
		}

		private static AuraEffect ParseAuraEffect(JObject obj)
		{
			if (obj == null) return null;
			var e = new AuraEffect();
			e.duration = obj["duration"]?.Value<int?>() ?? 0;
			e.radius = obj["radius"]?.Value<int?>() ?? 0;
			var eff = obj["effect"] as JObject;
			if (eff != null)
			{
				e.effect = new AuraStats { armor = eff["armor"]?.Value<int?>() ?? 0 };
			}
			e.targets = obj["targets"]?.Value<string>();
			return e;
		}

		private static MultiHitEffect ParseMultiHitEffect(JObject obj)
		{
			if (obj == null) return null;
			return new MultiHitEffect
			{
				count = obj["count"]?.Value<int?>() ?? 0,
				damage = obj["damage"]?.Value<int?>() ?? 0,
				interval = obj["interval"]?.Value<int?>() ?? 0,
				iframes = obj["iframes"]?.Value<int?>() ?? 0
			};
		}

		private static int ExtractWindUpMs(JObject actionObj)
		{
			if (actionObj == null) return 0;
			// Prefer action-level windUp if present
			var top = actionObj["windUp"]?.Value<int?>();
			if (top.HasValue) return top.Value;
			// Otherwise check nested attack.windUp
			var attackObj = actionObj["attack"] as JObject;
			if (attackObj != null)
			{
				var nested = attackObj["windUp"]?.Value<int?>();
				if (nested.HasValue) return nested.Value;
			}
			return 0;
		}

		private static int ExtractCooldownMs(JObject actionObj)
		{
			if (actionObj == null) return 0;
			var top = actionObj["cooldown"]?.Value<int?>();
			if (top.HasValue) return top.Value;
			var attackObj = actionObj["attack"] as JObject;
			if (attackObj != null)
			{
				var nested = attackObj["cooldown"]?.Value<int?>();
				if (nested.HasValue) return nested.Value;
			}
			return 0;
		}

		private static int ExtractManaCost(JObject actionObj)
		{
			if (actionObj == null) return 0;
			var top = actionObj["manaCost"]?.Value<int?>();
			if (top.HasValue) return top.Value;
			var attackObj = actionObj["attack"] as JObject;
			if (attackObj != null)
			{
				var nested = attackObj["manaCost"]?.Value<int?>();
				if (nested.HasValue) return nested.Value;
			}
			return 0;
		}

		public int GetWindUpMs(string pieceId, int actionIndex)
		{
			var data = GetData(pieceId);
			if (data == null || data.actions == null) return 0;
			if (actionIndex < 0 || actionIndex >= data.actions.Length) return 0;
			return data.actions[actionIndex] != null ? data.actions[actionIndex].windUpMs : 0;
		}

		public MoveEffect GetDefaultMove(string pieceId)
		{
			var data = GetData(pieceId);
			return data != null ? data.move : null;
		}

		// JSON helpers kept out intentionally to avoid wiping prefab assignments
	}
}
