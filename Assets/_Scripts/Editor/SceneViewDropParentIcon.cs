using UnityEditor;
using UnityEngine;

namespace ManaGambit.EditorTools
{
	/// <summary>
	/// Scene view helper that draws a configurable sprite over the currently selected Transform
	/// if it has the configured tag. Supports drag-and-drop: dropping any Transform/GameObject onto
	/// the sprite will make it a child of the selected Transform and reset local position/rotation.
	/// </summary>
	[InitializeOnLoad]
	public static class SceneViewDropParentIcon
	{
		private const string EditorPrefsPrefix = "MG_DropParent_";
		private const string TagKey = EditorPrefsPrefix + "Tag";
		private const string SpritePathKey = EditorPrefsPrefix + "SpritePath";
		private const string IconSizeKey = EditorPrefsPrefix + "IconSize";
		private const string EnabledKey = EditorPrefsPrefix + "Enabled";
		private const string OffsetXKey = EditorPrefsPrefix + "OffsetX";
		private const string OffsetYKey = EditorPrefsPrefix + "OffsetY";
		private const string OffsetZKey = EditorPrefsPrefix + "OffsetZ";
		private const float DefaultIconSize = 32f;
		private const float ScreenClampPadding = 8f;
		private static readonly Vector3 DefaultWorldScale = Vector3.one;

		static SceneViewDropParentIcon()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
			SceneView.duringSceneGui += OnSceneGUI;
		}

		public static string ConfiguredTag
		{
			get { return EditorPrefs.GetString(TagKey, string.Empty); }
			set { EditorPrefs.SetString(TagKey, value ?? string.Empty); }
		}

		public static float IconSize
		{
			get { return EditorPrefs.GetFloat(IconSizeKey, DefaultIconSize); }
			set { EditorPrefs.SetFloat(IconSizeKey, Mathf.Max(16f, value)); }
		}

		public static Sprite IconSprite
		{
			get
			{
				var path = EditorPrefs.GetString(SpritePathKey, string.Empty);
				if (string.IsNullOrEmpty(path)) return null;
				return AssetDatabase.LoadAssetAtPath<Sprite>(path);
			}
			set
			{
				var path = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
				EditorPrefs.SetString(SpritePathKey, path);
			}
		}

		public static bool IsEnabled
		{
			get { return EditorPrefs.GetBool(EnabledKey, true); }
			set { EditorPrefs.SetBool(EnabledKey, value); }
		}

		public static Vector3 IconOffset
		{
			get 
			{ 
				return new Vector3(
					EditorPrefs.GetFloat(OffsetXKey, 0f),
					EditorPrefs.GetFloat(OffsetYKey, 0f),
					EditorPrefs.GetFloat(OffsetZKey, 0f)
				);
			}
			set 
			{ 
				EditorPrefs.SetFloat(OffsetXKey, value.x);
				EditorPrefs.SetFloat(OffsetYKey, value.y);
				EditorPrefs.SetFloat(OffsetZKey, value.z);
			}
		}

		[MenuItem("Tools/Parent Drop Icon Settings")] 
		private static void OpenSettingsWindow()
		{
			ParentDropIconSettingsWindow.ShowWindow();
		}

		[MenuItem("Tools/Auto Assign to Drop Parents")] 
		public static void AutoAssignSelectedObjects()
		{
			var selectedObjects = Selection.gameObjects;
			if (selectedObjects.Length == 0)
			{
				EditorUtility.DisplayDialog("No Selection", "Please select one or more GameObjects in the hierarchy to auto assign.", "OK");
				return;
			}

			int assignedCount = 0;
			int skippedCount = 0;

			foreach (var selectedObject in selectedObjects)
			{
				if (selectedObject == null) continue;

				var closestParent = FindClosestDropParent(selectedObject.transform);
				if (closestParent != null)
				{
					// Check if this would create a cycle
					if (closestParent.IsChildOf(selectedObject.transform))
					{
						Debug.LogWarning($"Skipping {selectedObject.name} - would create parenting cycle with {closestParent.name}");
						skippedCount++;
						continue;
					}

					// Store the world scale before parenting
					Vector3 worldScale = selectedObject.transform.lossyScale;
					
					// Record the operation for undo
					Undo.SetTransformParent(selectedObject.transform, closestParent, "Auto Assign to Drop Parent");
					Undo.RecordObject(selectedObject.transform, "Apply transform settings");

					// Apply transform settings
					var dropTarget = closestParent.GetComponent<DropParentTarget>();
					if (dropTarget != null)
					{
						dropTarget.ApplyDefaultTransform(selectedObject.transform);
					}
					else
					{
						// Use global settings
						selectedObject.transform.localPosition = Vector3.zero;
						selectedObject.transform.localRotation = Quaternion.identity;
					}
					
					// Preserve the world scale by adjusting local scale
					// This ensures the object maintains its current world scale regardless of parent scale
					Vector3 parentLossyScale = closestParent.lossyScale;
					selectedObject.transform.localScale = new Vector3(
						worldScale.x / parentLossyScale.x,
						worldScale.y / parentLossyScale.y,
						worldScale.z / parentLossyScale.z
					);

					Debug.Log($"Auto assigned {selectedObject.name} to {closestParent.name}");
					assignedCount++;
				}
				else
				{
					Debug.LogWarning($"No valid drop parent found for {selectedObject.name}");
					skippedCount++;
				}
			}

			// Show results
			string message = $"Auto Assign Complete\n\nAssigned: {assignedCount}\nSkipped: {skippedCount}";
			if (assignedCount > 0)
			{
				message += "\n\nObjects have been assigned to their closest drop parent targets.";
			}
			else
			{
				message += "\n\nNo objects were assigned. Make sure you have GameObjects with DropParentTarget components or the configured tag in your scene.";
			}

			EditorUtility.DisplayDialog("Auto Assign Results", message, "OK");
		}

		private static Transform FindClosestDropParent(Transform targetTransform)
		{
			// First, look for component-based drop targets
			var dropTargets = Object.FindObjectsOfType<DropParentTarget>();
			Transform closestComponentTarget = null;
			float closestComponentDistance = float.MaxValue;

			foreach (var dropTarget in dropTargets)
			{
				if (dropTarget == null || !dropTarget.IsValidTarget()) continue;

				var dropTargetTransform = dropTarget.transform;
				if (dropTargetTransform == targetTransform) continue; // Skip self

				// Check if this drop target is a parent of the target
				if (dropTargetTransform.IsChildOf(targetTransform)) continue; // Skip if would create cycle

				// Calculate distance
				float distance = Vector3.Distance(targetTransform.position, dropTargetTransform.position);
				if (distance < closestComponentDistance)
				{
					closestComponentDistance = distance;
					closestComponentTarget = dropTargetTransform;
				}
			}

			// If we found a component-based target, return it
			if (closestComponentTarget != null)
			{
				return closestComponentTarget;
			}

			// Fallback to global tag-based targets
			if (!string.IsNullOrEmpty(ConfiguredTag))
			{
				var taggedObjects = GameObject.FindGameObjectsWithTag(ConfiguredTag);
				Transform closestTaggedTarget = null;
				float closestTaggedDistance = float.MaxValue;

				foreach (var taggedObject in taggedObjects)
				{
					if (taggedObject == null) continue;
					
					// Skip if this object has a DropParentTarget component (already checked above)
					if (taggedObject.GetComponent<DropParentTarget>() != null) continue;

					var taggedTransform = taggedObject.transform;
					if (taggedTransform == targetTransform) continue; // Skip self

					// Check if this tagged object is a parent of the target
					if (taggedTransform.IsChildOf(targetTransform)) continue; // Skip if would create cycle

					// Calculate distance
					float distance = Vector3.Distance(targetTransform.position, taggedTransform.position);
					if (distance < closestTaggedDistance)
					{
						closestTaggedDistance = distance;
						closestTaggedTarget = taggedTransform;
					}
				}

				return closestTaggedTarget;
			}

			return null;
		}

		private static void OnSceneGUI(SceneView sceneView)
		{
			// First, handle component-based drop targets
			HandleComponentBasedTargets(sceneView);
			
			// Then handle global settings (fallback)
			if (IsEnabled && IconSprite != null && !string.IsNullOrEmpty(ConfiguredTag))
			{
				HandleGlobalSettingsTargets(sceneView);
			}
		}

		private static void HandleComponentBasedTargets(SceneView sceneView)
		{
			// Respect global enabled setting for component-based targets too
			if (!IsEnabled) return;

			// Find all DropParentTarget components in the scene
			var dropTargets = Object.FindObjectsOfType<DropParentTarget>();
			if (dropTargets.Length == 0) return;

			foreach (var dropTarget in dropTargets)
			{
				if (dropTarget == null) continue; // Skip destroyed objects
				if (!dropTarget.IsValidTarget()) continue; // Skip if not valid
				
				var transform = dropTarget.transform;
				var iconSprite = dropTarget.IconSprite;
				if (iconSprite == null) continue; // Skip if no icon sprite
				
				// World -> GUI position with offset
				var worldPos = transform.position + dropTarget.IconOffset;
				var guiPos = HandleUtility.WorldToGUIPoint(worldPos);

				var size = dropTarget.IconSize;
				var iconRect = new Rect(guiPos.x - size * 0.5f, guiPos.y - size * 0.5f, size, size);
				
				// Check if the icon is completely outside the Scene view bounds
				if (iconRect.xMax < 0 || iconRect.xMin > sceneView.position.width ||
					iconRect.yMax < 0 || iconRect.yMin > sceneView.position.height)
				{
					continue; // Skip this icon if it's outside the view
				}

				Handles.BeginGUI();
				DrawSprite(iconRect, iconSprite);
				Handles.EndGUI();
				
				HandleDragAndDrop(iconRect, transform, dropTarget);
			}
		}

		private static void HandleGlobalSettingsTargets(SceneView sceneView)
		{
			// Find all GameObjects in the scene with the configured tag
			var taggedObjects = GameObject.FindGameObjectsWithTag(ConfiguredTag);
			if (taggedObjects.Length == 0) return;

			foreach (var go in taggedObjects)
			{
				if (go == null) continue; // Skip destroyed objects
				
				// Skip if this object has a DropParentTarget component (already handled above)
				if (go.GetComponent<DropParentTarget>() != null) continue;
				
				var transform = go.transform;
				// World -> GUI position with offset
				var worldPos = transform.position + IconOffset;
				var guiPos = HandleUtility.WorldToGUIPoint(worldPos);

				var size = IconSize;
				var iconRect = new Rect(guiPos.x - size * 0.5f, guiPos.y - size * 0.5f, size, size);
				
				// Check if the icon is completely outside the Scene view bounds
				if (iconRect.xMax < 0 || iconRect.xMin > sceneView.position.width ||
					iconRect.yMax < 0 || iconRect.yMin > sceneView.position.height)
				{
					continue; // Skip this icon if it's outside the view
				}

				Handles.BeginGUI();
				DrawSprite(iconRect, IconSprite);
				Handles.EndGUI();
				
				HandleDragAndDrop(iconRect, transform, null);
			}
		}

		private static void DrawSprite(Rect rect, Sprite sprite)
		{
			if (sprite == null) return;
			var texture = sprite.texture;
			if (texture == null) return;

			var r = sprite.rect;
			var texCoords = new Rect(
				r.x / texture.width,
				r.y / texture.height,
				r.width / texture.width,
				r.height / texture.height
			);

			GUI.DrawTextureWithTexCoords(rect, texture, texCoords, true);
		}



		private static void HandleDragAndDrop(Rect iconRect, Transform targetParent, DropParentTarget dropTarget = null)
		{
			var evt = Event.current;
			if (evt == null) return;

			// Check if mouse is over the icon
			if (!iconRect.Contains(evt.mousePosition)) return;

			// Check if we have valid objects to drop (scene objects or prefabs)
			bool hasValidObjects = false;
			bool hasPrefabs = false;
			foreach (var obj in DragAndDrop.objectReferences)
			{
				if (obj is GameObject)
				{
					if (EditorUtility.IsPersistent(obj))
					{
						// This is a prefab
						hasPrefabs = true;
						hasValidObjects = true;
					}
					else
					{
						// This is a scene object
						hasValidObjects = true;
					}
				}
				else if (obj is Transform && !EditorUtility.IsPersistent(obj))
				{
					// This is a scene transform
					hasValidObjects = true;
				}
			}

			if (!hasValidObjects) return;

			switch (evt.type)
			{
				case EventType.DragUpdated:
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Link;
					Event.current.Use();
					break;
				}
				case EventType.DragPerform:
				{
					Debug.Log($"Dropping {DragAndDrop.objectReferences.Length} objects onto {targetParent.name}");
					
					// Store the objects we want to reparent (scene objects)
					var objectsToReparent = new System.Collections.Generic.List<Transform>();
					// Store prefabs to instantiate
					var prefabsToInstantiate = new System.Collections.Generic.List<GameObject>();
					
					for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
					{
						var obj = DragAndDrop.objectReferences[i];
						
						if (obj is Transform t && !EditorUtility.IsPersistent(t))
						{
							// Scene transform
							if (t == targetParent) continue; // cannot parent to self
							if (targetParent != null && targetParent.IsChildOf(t)) continue; // avoid cycles
							objectsToReparent.Add(t);
						}
						else if (obj is GameObject go)
						{
							if (EditorUtility.IsPersistent(go))
							{
								// Prefab - will instantiate
								prefabsToInstantiate.Add(go);
							}
							else
							{
								// Scene GameObject
								var childTransform = go.transform;
								if (childTransform == targetParent) continue; // cannot parent to self
								if (targetParent != null && targetParent.IsChildOf(childTransform)) continue; // avoid cycles
								objectsToReparent.Add(childTransform);
							}
						}
					}

					DragAndDrop.AcceptDrag();
					Event.current.Use();

					// First, instantiate prefabs
					foreach (var prefab in prefabsToInstantiate)
					{
						Debug.Log($"Instantiating prefab {prefab.name} as child of {targetParent.name}");
						
						// Instantiate the prefab in world space first (not as child)
						var instantiatedObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
						var instantiatedTransform = instantiatedObject.transform;
						
						// Record the instantiation for undo
						Undo.RegisterCreatedObjectUndo(instantiatedObject, "Instantiate Prefab via Scene Drop Icon");
						
						// Store the world scale before parenting
						Vector3 worldScale = instantiatedTransform.lossyScale;
						
						// Now parent to the target
						Undo.SetTransformParent(instantiatedTransform, targetParent, "Parent instantiated prefab");
						
						// Apply transform settings to the instantiated object
						Undo.RecordObject(instantiatedTransform, "Apply transform settings to instantiated prefab");
						
						if (dropTarget != null)
						{
							dropTarget.ApplyDefaultTransform(instantiatedTransform);
						}
						else
						{
							instantiatedTransform.localPosition = Vector3.zero;
							instantiatedTransform.localRotation = Quaternion.identity;
						}
						
						// Preserve the world scale by adjusting local scale
						// This ensures the prefab maintains its intended world scale (1,1,1) regardless of parent scale
						Vector3 parentLossyScale = targetParent.lossyScale;
						instantiatedTransform.localScale = new Vector3(
							DefaultWorldScale.x / parentLossyScale.x,
							DefaultWorldScale.y / parentLossyScale.y,
							DefaultWorldScale.z / parentLossyScale.z
						);
					}

					// Then, reparent existing scene objects
					foreach (var childTransform in objectsToReparent)
					{
						Debug.Log($"Reparenting {childTransform.name} to {targetParent.name}");
						Undo.SetTransformParent(childTransform, targetParent, "Reparent via Scene Drop Icon");
						Undo.RecordObject(childTransform, "Apply transform settings");
						
						// Use component settings if available, otherwise use defaults
						if (dropTarget != null)
						{
							dropTarget.ApplyDefaultTransform(childTransform);
						}
						else
						{
							childTransform.localPosition = Vector3.zero;
							childTransform.localRotation = Quaternion.identity;
						}
					}
					break;
				}
			}
		}
	}

	public class ParentDropIconSettingsWindow : EditorWindow
	{
		private const float MinSize = 16f;
		private const float MaxSize = 256f;

		private string tagValue;
		private Sprite spriteValue;
		private float sizeValue;
		private bool enabledValue;
		private Vector3 offsetValue;

		public static void ShowWindow()
		{
			var window = GetWindow<ParentDropIconSettingsWindow>(true, "Parent Drop Icon Settings");
			window.minSize = new Vector2(320, 160);
			window.Show();
		}

		private void OnEnable()
		{
			tagValue = SceneViewDropParentIcon.ConfiguredTag;
			spriteValue = SceneViewDropParentIcon.IconSprite;
			sizeValue = SceneViewDropParentIcon.IconSize;
			enabledValue = SceneViewDropParentIcon.IsEnabled;
			offsetValue = SceneViewDropParentIcon.IconOffset;
		}

		private void OnGUI()
		{
			bool needsRepaint = false;

			bool newEnabledValue = EditorGUILayout.Toggle("Enable Icons", enabledValue);
			if (newEnabledValue != enabledValue)
			{
				enabledValue = newEnabledValue;
				needsRepaint = true;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Show icon when selected object has tag", EditorStyles.boldLabel);
			string newTagValue = EditorGUILayout.TagField("Tag", string.IsNullOrEmpty(tagValue) ? "Untagged" : tagValue);
			if (newTagValue != tagValue)
			{
				tagValue = newTagValue;
				needsRepaint = true;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Icon Sprite", EditorStyles.boldLabel);
			Sprite newSpriteValue = (Sprite)EditorGUILayout.ObjectField("Sprite", spriteValue, typeof(Sprite), false);
			if (newSpriteValue != spriteValue)
			{
				spriteValue = newSpriteValue;
				needsRepaint = true;
			}

			EditorGUILayout.Space();
			float newSizeValue = EditorGUILayout.Slider("Icon Size (px)", sizeValue, MinSize, MaxSize);
			if (!Mathf.Approximately(newSizeValue, sizeValue))
			{
				sizeValue = newSizeValue;
				needsRepaint = true;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Icon Position Offset", EditorStyles.boldLabel);
			Vector3 newOffsetValue = EditorGUILayout.Vector3Field("Offset", offsetValue);
			if (newOffsetValue != offsetValue)
			{
				offsetValue = newOffsetValue;
				needsRepaint = true;
			}

			// Repaint scene view immediately when values change
			if (needsRepaint)
			{
				SceneView.RepaintAll();
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Auto Assign", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Select GameObjects in the hierarchy and click 'Auto Assign' to automatically assign them to the closest drop parent target.", MessageType.Info);
			
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Auto Assign", GUILayout.Width(100)))
				{
					SceneViewDropParentIcon.AutoAssignSelectedObjects();
				}
			}

			GUILayout.FlexibleSpace();
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Save", GUILayout.Width(100)))
				{
					SceneViewDropParentIcon.IsEnabled = enabledValue;
					SceneViewDropParentIcon.ConfiguredTag = tagValue == "Untagged" ? string.Empty : tagValue;
					SceneViewDropParentIcon.IconSprite = spriteValue;
					SceneViewDropParentIcon.IconSize = sizeValue;
					SceneViewDropParentIcon.IconOffset = offsetValue;
					Repaint();
					SceneView.RepaintAll();
					Close();
				}
			}
		}
	}
}


