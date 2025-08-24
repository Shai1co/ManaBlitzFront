
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
		}

		[System.Serializable]
		public class UnitData
		{
			public string pieceId;
			public GameObject prefab;  // Manual assignment
			public int hp;
			public int armor;
			public int magicArmor;
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
					icon = obj["icon"]?.Value<string>()
				};
				list.Add(info);
			}
			return list.ToArray();
		}

		// JSON helpers kept out intentionally to avoid wiping prefab assignments
	}
}
