using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Finds types where the same serialized field name exists multiple times in the inheritance chain
public static class SerializationDiagnostics
{
    [MenuItem("Tools/Diagnostics/List Duplicate Serialized Field Names")] 
    private static void ListDuplicateSerializedFields()
    {
        int problems = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (!typeof(UnityEngine.Object).IsAssignableFrom(t)) continue; // MonoBehaviour/ScriptableObject
                if (t.IsAbstract) continue;

                // Collect serialized field names for this type and its base types
                var names = new List<string>();
                var typeCursor = t;
                while (typeCursor != null && typeof(UnityEngine.Object).IsAssignableFrom(typeCursor))
                {
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    foreach (var f in typeCursor.GetFields(flags))
                    {
                        if (IsUnitySerializedField(f))
                        {
                            names.Add(f.Name);
                        }
                    }
                    typeCursor = typeCursor.BaseType;
                }

                var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
                if (dupes.Length > 0)
                {
                    problems++;
                    Debug.LogError($"[SerializationDiagnostics] Type '{t.FullName}' has duplicate serialized field name(s): {string.Join(", ", dupes)}");
                }
            }
        }

        if (problems == 0)
        {
            Debug.Log("[SerializationDiagnostics] No duplicate serialized field names found in loaded assemblies.");
        }
        else
        {
            Debug.LogWarning($"[SerializationDiagnostics] Detected {problems} type(s) with duplicate serialized field names. See errors above.");
        }
    }

    private static bool IsUnitySerializedField(FieldInfo field)
    {
        // Unity serializes: public non-static fields, or fields with [SerializeField]; but not [NonSerialized]
        if (field.IsStatic) return false;
        if (field.GetCustomAttribute<NonSerializedAttribute>() != null) return false;
        if (field.IsPublic) return true;
        if (field.GetCustomAttribute<SerializeField>() != null) return true;
        return false;
    }
}


