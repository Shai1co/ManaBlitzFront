using UnityEngine;
using Sirenix.OdinInspector;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace ManaGambit
{
	// Split to a partial to keep the main file smaller per user preference
	public partial class UnitConfig : ScriptableObject
	{
		private const string DefaultJsonFileNameWithoutExtension = "characters";
		private const string JsonFileExtension = ".json";

		[Button]
		public void SaveToJson()
		{
			if (units == null || units.Length == 0)
			{
				Debug.LogWarning("UnitConfig.SaveToJson: No units to save. Did you run LoadFromJson()? ");
				return;
			}

			bool wrapWithDataKey = DetermineUseDataWrapperFromCurrentAsset();
			var rootObject = BuildUnitsJsonObject(wrapWithDataKey);
			string json = rootObject.ToString(Newtonsoft.Json.Formatting.Indented);

			#if UNITY_EDITOR
			// Pre-save warning: inform that choosing the assigned asset path will overwrite it
			string assignedAssetPath = jsonAsset != null ? UnityEditor.AssetDatabase.GetAssetPath(jsonAsset) : null;
			string assignedFullPath = null;
			if (!string.IsNullOrEmpty(assignedAssetPath))
			{
				string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
				assignedFullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, assignedAssetPath));
			}
			string assignedDisplay = !string.IsNullOrEmpty(assignedAssetPath) ? assignedAssetPath : "<none assigned>";
			bool continueExport = UnityEditor.EditorUtility.DisplayDialog(
				"Save UnitConfig JSON",
				"This will export the current UnitConfig to JSON.\n\nIf you choose the same path as the assigned JSON asset, it will be overwritten.\n\nAssigned JSON: " + assignedDisplay + "\n\nContinue?",
				"Continue",
				"Cancel");
			if (!continueExport) return;

			string suggestedName = jsonAsset != null && !string.IsNullOrWhiteSpace(jsonAsset.name)
				? jsonAsset.name
				: DefaultJsonFileNameWithoutExtension;
			string folder = UnityEditor.EditorUtility.SaveFolderPanel("Select folder to save UnitConfig JSON", Application.dataPath, "");
			if (string.IsNullOrEmpty(folder)) return;
			string filePath = Path.Combine(folder, suggestedName + JsonFileExtension);

			// If the chosen path targets the assigned JSON asset, ask for explicit overwrite confirmation
			if (!string.IsNullOrEmpty(assignedFullPath))
			{
				string targetFullPath = System.IO.Path.GetFullPath(filePath);
				bool isOverwritingAssigned = string.Equals(targetFullPath, assignedFullPath, System.StringComparison.OrdinalIgnoreCase);
				if (isOverwritingAssigned)
				{
					bool confirmOverwrite = UnityEditor.EditorUtility.DisplayDialog(
						"Overwrite Assigned JSON?",
						"You are about to overwrite the assigned JSON asset:\n" + assignedAssetPath + "\n\nProceed?",
						"Overwrite",
						"Cancel");
					if (!confirmOverwrite) return;
				}
			}
			try
			{
				File.WriteAllText(filePath, json, Encoding.UTF8);
				Debug.Log($"UnitConfig.SaveToJson: Saved to '{filePath}'.");
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"UnitConfig.SaveToJson: Failed to write file. {ex.Message}");
			}
			#else
			Debug.LogWarning("UnitConfig.SaveToJson is only available in the Unity Editor.");
			#endif
		}

		private bool DetermineUseDataWrapperFromCurrentAsset()
		{
			if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text)) return true;
			try
			{
				var parsed = JObject.Parse(jsonAsset.text);
				var dataToken = parsed[JsonRootDataKey];
				return dataToken != null && dataToken.Type == JTokenType.Object;
			}
			catch
			{
				return true; // default to wrapped if unsure
			}
		}

		private JObject BuildUnitsJsonObject(bool wrap)
		{
			var unitsObject = new JObject();
			if (units != null)
			{
				// Stable ordering for determinism
				var sorted = new List<UnitData>(units);
				sorted.Sort((a, b) => string.CompareOrdinal(a?.pieceId, b?.pieceId));
				for (int i = 0; i < sorted.Count; i++)
				{
					var u = sorted[i];
					if (u == null || string.IsNullOrEmpty(u.pieceId)) continue;
					unitsObject[u.pieceId] = BuildUnitObject(u);
				}
			}

			if (wrap)
			{
				var root = new JObject();
				root[JsonRootDataKey] = unitsObject;
				return root;
			}
			return unitsObject;
		}

		private static JObject BuildUnitObject(UnitData unit)
		{
			var obj = new JObject
			{
				["hp"] = unit.hp,
				["armor"] = unit.armor,
				["magicArmor"] = unit.magicArmor,
				["critical"] = unit.critical,
				["dodge"] = unit.dodge,
				["hpRegenPerSecond"] = unit.hpRegenPerSecond,
				["moveSpeed"] = unit.moveSpeed,
				["priority"] = unit.priority
			};

			if (unit.move != null) obj["move"] = BuildMoveEffectObject(unit.move);

			var actionsArray = new JArray();
			if (unit.actions != null)
			{
				for (int i = 0; i < unit.actions.Length; i++)
				{
					var action = unit.actions[i];
					if (action == null) { actionsArray.Add(new JObject()); continue; }
					actionsArray.Add(BuildActionObject(action));
				}
			}
			obj["actions"] = actionsArray;
			return obj;
		}

		private static JObject BuildActionObject(ActionInfo action)
		{
			var obj = new JObject();
			if (!string.IsNullOrEmpty(action.name)) obj["name"] = action.name;
			if (!string.IsNullOrEmpty(action.shortDisplayName)) obj["shortDisplayName"] = action.shortDisplayName;
			if (!string.IsNullOrEmpty(action.type)) obj["type"] = action.type;
			if (!string.IsNullOrEmpty(action.icon)) obj["icon"] = action.icon;

			// Use top-level timing and cost keys to align with loader support
			if (action.windUpMs != 0) obj["windUp"] = action.windUpMs;
			if (action.cooldownMs != 0) obj["cooldown"] = action.cooldownMs;
			if (action.manaCost != 0) obj["manaCost"] = action.manaCost;

			if (action.attack != null) obj["attack"] = BuildAttackEffectObject(action.attack);
			if (action.move != null) obj["move"] = BuildMoveEffectObject(action.move);
			if (action.swap != null) obj["swap"] = BuildSwapEffectObject(action.swap);
			if (action.buff != null) obj["buff"] = BuildBuffEffectObject(action.buff);
			if (action.aura != null) obj["aura"] = BuildAuraEffectObject(action.aura);
			if (action.multiHit != null) obj["multiHit"] = BuildMultiHitEffectObject(action.multiHit);

			return obj;
		}

		private static JObject BuildMoveEffectObject(MoveEffect e)
		{
			var obj = new JObject
			{
				["manaCost"] = e.manaCost,
				["overFriendly"] = e.overFriendly,
				["overEnemy"] = e.overEnemy,
				["targetFriendly"] = e.targetFriendly
			};
			if (e.patterns != null && e.patterns.Count > 0) obj["patterns"] = BuildMovePatternsArray(e.patterns);
			if (e.postImpact != null) obj["postImpact"] = BuildPostImpactObject(e.postImpact);
			return obj;
		}

		private static JArray BuildMovePatternsArray(List<MovePattern> patterns)
		{
			var arr = new JArray();
			for (int i = 0; i < patterns.Count; i++)
			{
				var p = patterns[i];
				if (p == null) { arr.Add(new JObject()); continue; }
				var pObj = new JObject();
				if (!string.IsNullOrEmpty(p.dir)) pObj["dir"] = p.dir;
				pObj["range"] = new JArray(p.rangeMin, p.rangeMax);
				if (p.leap) pObj["leap"] = true;
				if (p.stopOnHit) pObj["stopOnHit"] = true;
				if (p.passThrough) pObj["passThrough"] = true;
				arr.Add(pObj);
			}
			return arr;
		}

		private static JObject BuildPostImpactObject(PostImpact p)
		{
			var obj = new JObject();
			if (!string.IsNullOrEmpty(p.behavior)) obj["behavior"] = p.behavior;
			obj["radius"] = p.radius;
			if (p.random) obj["random"] = true;
			return obj;
		}

		private static JObject BuildAttackEffectObject(AttackEffect e)
		{
			var obj = new JObject
			{
				["damage"] = e.damage,
				["speed"] = e.speed
			};
			if (!string.IsNullOrEmpty(e.damageType)) obj["type"] = e.damageType;
			if (e.patterns != null && e.patterns.Count > 0) obj["patterns"] = BuildMovePatternsArray(e.patterns);
			if (e.overFriendly) obj["overFriendly"] = true;
			if (e.overEnemy) obj["overEnemy"] = true;
			if (e.stopOnHit) obj["stopOnHit"] = true;
			if (e.maxTargets != 0) obj["maxTargets"] = e.maxTargets;
			if (e.pierce != 0) obj["pierce"] = e.pierce;
			if (e.knockback != 0) obj["knockback"] = e.knockback;
			if (e.aoePattern != null)
			{
				var ap = new JObject();
				if (!string.IsNullOrEmpty(e.aoePattern.dir)) ap["dir"] = e.aoePattern.dir;
				ap["range"] = e.aoePattern.range;
				obj["aoePattern"] = ap;
			}
			if (e.aoeDamage != null)
			{
				var ad = new JObject { ["center"] = e.aoeDamage.center, ["adjacent"] = e.aoeDamage.adjacent };
				obj["aoeDamage"] = ad;
			}
			if (e.dot != null)
			{
				var dot = new JObject { ["damage"] = e.dot.damage, ["interval"] = e.dot.interval, ["duration"] = e.dot.duration };
				obj["dot"] = dot;
			}
			if (e.debuff != null)
			{
				var deb = new JObject { ["speedReduction"] = e.debuff.speedReduction, ["duration"] = e.debuff.duration };
				obj["debuff"] = deb;
			}
			if (e.amount != 0) obj["amount"] = e.amount;
			if (e.interval != 0) obj["interval"] = e.interval;
			if (e.duration != 0) obj["duration"] = e.duration;
			if (e.requireLos) obj["requireLos"] = true;
			if (e.allowFriendlyTarget) obj["allowFriendlyTarget"] = true;
			return obj;
		}

		private static JObject BuildSwapEffectObject(SwapEffect e)
		{
			var obj = new JObject
			{
				["manaCost"] = e.manaCost,
				["windUp"] = e.windUp,
				["cooldown"] = e.cooldown,
				["overFriendly"] = e.overFriendly,
				["overEnemy"] = e.overEnemy,
				["targetFriendly"] = e.targetFriendly
			};
			if (e.patterns != null && e.patterns.Count > 0) obj["patterns"] = BuildMovePatternsArray(e.patterns);
			return obj;
		}

		private static JObject BuildBuffEffectObject(BuffEffect e)
		{
			var obj = new JObject
			{
				["duration"] = e.duration
			};
			if (e.effect != null)
			{
				var eff = new JObject
				{
					["rangeBonus"] = e.effect.rangeBonus,
					["gcdReduction"] = e.effect.gcdReduction,
					["shotCount"] = e.effect.shotCount,
					["moveSpeedBonus"] = e.effect.moveSpeedBonus,
					["manaCostReduction"] = e.effect.manaCostReduction,
					["pierce"] = e.effect.pierce
				};
				obj["effect"] = eff;
			}
			if (!string.IsNullOrEmpty(e.targets)) obj["targets"] = e.targets;
			return obj;
		}

		private static JObject BuildAuraEffectObject(AuraEffect e)
		{
			var obj = new JObject
			{
				["duration"] = e.duration,
				["radius"] = e.radius
			};
			if (e.effect != null)
			{
				obj["effect"] = new JObject { ["armor"] = e.effect.armor };
			}
			if (!string.IsNullOrEmpty(e.targets)) obj["targets"] = e.targets;
			return obj;
		}

		private static JObject BuildMultiHitEffectObject(MultiHitEffect e)
		{
			return new JObject
			{
				["count"] = e.count,
				["damage"] = e.damage,
				["interval"] = e.interval,
				["iframes"] = e.iframes
			};
		}
	}
}


