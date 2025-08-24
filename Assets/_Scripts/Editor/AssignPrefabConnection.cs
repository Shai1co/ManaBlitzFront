// 8/22/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
// 8/22/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.


public class AssignPrefabConnection : MonoBehaviour
{
    [MenuItem("Tools/Assign Prefab Connection to Selected Objects")]
    public static void AssignPrefabToSelectedObjects()
    {
        // Get all selected GameObjects in the Hierarchy
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogError("No objects selected. Please select one or more objects in the Hierarchy.");
            return;
        }

        string prefabPath = "Assets/_Prefabs/0_0.prefab"; // Replace with your prefab's path
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            Debug.LogError("Prefab not found at the specified path.");
            return;
        }

        // Iterate through all selected objects and assign the prefab connection
        foreach (GameObject selectedObject in selectedObjects)
        {
            if (selectedObject == null)
                continue;

            PrefabUtility.SaveAsPrefabAssetAndConnect(selectedObject, prefabPath, InteractionMode.UserAction);
            Debug.Log($"Assigned prefab connection to {selectedObject.name} without changing its name.");
        }

        Debug.Log("Prefab connection assigned to all selected objects.");
    }
}
