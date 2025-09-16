using UnityEditor;
using UnityEngine;

namespace ManaGambit
{
	[CustomEditor(typeof(SkillVfxPreset))]
	public class SkillVfxPresetEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			// Draw all fields except the new ones, which we'll draw manually
			DrawPropertiesExcluding(serializedObject, 
				"windupLookAtCamera", 
				"projectileLookAtCamera", 
				"impactLookAtCamera",
				"attackMoveOffsetWorldUnits",
				"auraPrefab",
				"auraAttach",
				"auraLookAtCamera");

			var preset = target as SkillVfxPreset;

			// Manually draw the new fields in the desired positions
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windupLookAtCamera"));
			if (preset.ProjectilePrefab != null)
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileLookAtCamera"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileTarget"), new GUIContent("Projectile Target Anchor"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileArcDegrees"), new GUIContent("Projectile Arc Degrees"));
			}
			if (preset.ImpactPrefab != null)
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("impactLookAtCamera"));
			}
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Attack Movement", EditorStyles.boldLabel);
			var offsetProp = serializedObject.FindProperty("attackMoveOffsetWorldUnits");
			EditorGUILayout.PropertyField(offsetProp, new GUIContent("Attack Move Offset (world units)", "Offset from tile center toward the target for skill-driven movement. Normal moves unaffected."));

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Aura (Optional)", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("auraPrefab"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("auraAttach"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("auraLookAtCamera"));
 			
 			EditorGUILayout.Space();
 			EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
 			
 			using (new EditorGUILayout.HorizontalScope())
 			{
 				if (GUILayout.Button("Load From UnitConfig"))
 				{
 					LoadFromUnitConfig(preset);
 				}
 				if (GUILayout.Button("Ping UnitConfig"))
 				{
 					PingUnitConfig();
 				}
 			}
 			
 			serializedObject.ApplyModifiedProperties();
 		}

 		private static UnitConfig FindUnitConfig()
 		{
 			// Try to find any UnitConfig in the project
 			string[] guids = AssetDatabase.FindAssets("t:UnitConfig");
 			if (guids != null && guids.Length > 0)
 			{
 				string path = AssetDatabase.GUIDToAssetPath(guids[0]);
 				return AssetDatabase.LoadAssetAtPath<UnitConfig>(path);
 			}
 			return null;
 		}

 		private static void PingUnitConfig()
 		{
 			var cfg = FindUnitConfig();
 			if (cfg != null)
 			{
 				EditorGUIUtility.PingObject(cfg);
 			}
 			else
 			{
 				EditorUtility.DisplayDialog("UnitConfig", "No UnitConfig asset found.", "OK");
 			}
 		}

 		private static void LoadFromUnitConfig(SkillVfxPreset preset)
 		{
 			if (preset == null) return;
 			var cfg = FindUnitConfig();
 			if (cfg == null)
 			{
 				EditorUtility.DisplayDialog("Load From UnitConfig", "No UnitConfig found in project.", "OK");
 				return;
 			}
 			// Find the first piece that has an action with this name to read windup etc.
 			UnitConfig.UnitData[] units = cfg.units;
 			if (units == null || units.Length == 0)
 			{
 				EditorUtility.DisplayDialog("Load From UnitConfig", "UnitConfig has no units loaded.", "OK");
 				return;
 			}
 			for (int u = 0; u < units.Length; u++)
 			{
 				var data = units[u];
 				if (data == null || data.actions == null) continue;
 				for (int i = 0; i < data.actions.Length; i++)
 				{
 					var a = data.actions[i];
 					if (a != null && string.Equals(a.name, preset.ActionName))
 					{
 						Undo.RecordObject(preset, "Load From UnitConfig");
 						// Prefer FromTicks policy; set fixed as a convenience copy
 						preset.GetType().GetField("windupDurationPolicy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(preset, SkillVfxPreset.WindupDurationPolicy.FromTicks);
 						preset.GetType().GetField("fixedWindupMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(preset, a.windUpMs);
 						EditorUtility.SetDirty(preset);
 						AssetDatabase.SaveAssets();
 						EditorUtility.DisplayDialog("Load From UnitConfig", $"Loaded windup {a.windUpMs}ms for action '{a.name}'.", "OK");
 						return;
 					}
 				}
 			}
 			EditorUtility.DisplayDialog("Load From UnitConfig", $"Action '{preset.ActionName}' not found in UnitConfig.", "OK");
 		}

 		[MenuItem("Tools/ManaGambit/Generate Skill VFX Presets From UnitConfig")] 
 		private static void GeneratePresetsFromUnitConfig()
 		{
 			var cfg = FindUnitConfig();
 			if (cfg == null)
 			{
 				EditorUtility.DisplayDialog("Generate Presets", "No UnitConfig found in project.", "OK");
 				return;
 			}
 			if (cfg.units == null || cfg.units.Length == 0)
 			{
 				EditorUtility.DisplayDialog("Generate Presets", "UnitConfig has no units loaded.", "OK");
 				return;
 			}
 			string root = "Assets/VFX/AutoPresets";
 			if (!AssetDatabase.IsValidFolder(root))
 			{
 				AssetDatabase.CreateFolder("Assets", "VFX");
 				AssetDatabase.CreateFolder("Assets/VFX", "AutoPresets");
 			}
 			int created = 0;
 			for (int u = 0; u < cfg.units.Length; u++)
 			{
 				var data = cfg.units[u];
 				if (data == null || data.actions == null) continue;
 				for (int i = 0; i < data.actions.Length; i++)
 				{
 					var a = data.actions[i];
 					if (a == null || string.IsNullOrEmpty(a.name)) continue;
 					// Create or reuse preset asset per action name
 					string assetName = SanitizeFileName($"SkillVfxPreset_{data.pieceId}_{a.name}");
 					string path = $"{root}/{assetName}.asset";
 					var existing = AssetDatabase.LoadAssetAtPath<SkillVfxPreset>(path);
 					if (existing == null)
 					{
 						var preset = ScriptableObject.CreateInstance<SkillVfxPreset>();
 						// Set fields via reflection to avoid breaking private serializations
 						preset.GetType().GetField("actionName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(preset, a.name);
 						preset.GetType().GetField("windupDurationPolicy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(preset, SkillVfxPreset.WindupDurationPolicy.FromTicks);
 						preset.GetType().GetField("fixedWindupMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(preset, a.windUpMs);
 						AssetDatabase.CreateAsset(preset, path);
 						created++;
 					}
 				}
 			}
 			AssetDatabase.SaveAssets();
 			EditorUtility.DisplayDialog("Generate Presets", $"Generated/updated presets. Created: {created}", "OK");
 		}

 		private static string SanitizeFileName(string name)
 		{
 			foreach (char c in System.IO.Path.GetInvalidFileNameChars())
 			{
 				name = name.Replace(c.ToString(), "_");
 			}
 			return name;
 		}
 	}
}
