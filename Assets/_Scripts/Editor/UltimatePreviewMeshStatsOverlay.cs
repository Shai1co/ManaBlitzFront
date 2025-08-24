// Copyright (c) ManaGambit
// Adds a small text overlay to Ultimate Preview windows showing mesh vertex/triangle counts.

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManaGambit.EditorExtensions
{
	/// <summary>
	/// Injects a VisualElements label into Ultimate Preview windows to display
	/// mesh stats (vertices/triangles) for the currently previewed selection.
	/// </summary>
	[InitializeOnLoad]
	public static class UltimatePreviewMeshStatsOverlay
	{
		private const string OverlayRootName = "UP_MeshStatsOverlayRoot";
		private const string OverlayLabelName = "UP_MeshStatsOverlayLabel";
		private const float OverlayTopPadding = 6f; // Distance from window top edge
		private const float OverlaySidePadding = 10f; // Left/right insets
		private const string OverlayShadowLabelName = "UP_MeshStatsOverlayShadow";
		private static readonly Color OverlayTextColor = new Color(0.9f, 0.9f, 0.9f, 1f);
		private static readonly Color OverlayShadowColor = new Color(0f, 0f, 0f, 0.65f);

		static UltimatePreviewMeshStatsOverlay()
		{
			EditorApplication.update += Update;
			// Also refresh on selection changes immediately
			Selection.selectionChanged += ForceRefreshAllKnownWindows;
			EditorApplication.delayCall += ForceRefreshAllKnownWindows;
		}

		private static void Update()
		{
			// Attach overlays to any Ultimate Preview window instances
			var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
			for (int i = 0; i < windows.Length; i++)
			{
				EditorWindow win = windows[i];
				if (!IsUltimatePreviewWindow(win))
					continue;

				EnsureOverlay(win);
				UpdateOverlayText(win);
			}
		}

		private static bool IsUltimatePreviewWindow(EditorWindow win)
		{
			// Match by type name to avoid hard dependency on the plugin assembly
			// Known window type usually contains "UltimatePreviewWindow"
			string typeName = win.GetType().Name;
			if (typeName.IndexOf("UltimatePreviewWindow", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;

			// Fallback: match by title text to be resilient to type renames
			string title = win.titleContent != null ? win.titleContent.text : string.Empty;
			if (!string.IsNullOrEmpty(title))
			{
				if (title.IndexOf("Ultimate Preview", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
				// Broaden to any Preview-titled window to ensure coverage
				if (title.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}
			return false;
		}

		private static void EnsureOverlay(EditorWindow win)
		{
			VisualElement root = win.rootVisualElement;
			if (root == null)
				return;

			VisualElement overlayRoot = root.Q<VisualElement>(OverlayRootName);
			if (overlayRoot != null)
				return;

			// Create overlay container
			overlayRoot = new VisualElement { name = OverlayRootName };
			overlayRoot.style.position = Position.Absolute;
			overlayRoot.style.left = OverlaySidePadding;
			overlayRoot.style.right = OverlaySidePadding;
			overlayRoot.style.top = OverlayTopPadding;
			overlayRoot.style.height = StyleKeyword.Auto;
			overlayRoot.pickingMode = PickingMode.Ignore; // Let clicks pass through

			var label = new Label { name = OverlayLabelName };
			label.style.unityTextAlign = TextAnchor.UpperCenter;
			label.style.color = OverlayTextColor;
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.fontSize = 13;
			label.style.whiteSpace = WhiteSpace.NoWrap;
			label.style.opacity = 0.98f;
			label.pickingMode = PickingMode.Ignore;

			// Shadow effect by duplicating the label underneath
			var shadow = new Label { name = OverlayShadowLabelName };
			shadow.style.unityTextAlign = TextAnchor.UpperCenter;
			shadow.style.color = OverlayShadowColor;
			shadow.style.unityFontStyleAndWeight = FontStyle.Bold;
			shadow.style.fontSize = 13;
			shadow.style.whiteSpace = WhiteSpace.NoWrap;
			shadow.style.position = Position.Absolute;
			shadow.style.left = 1;
			shadow.style.top = 1;
			shadow.style.right = 0;
			shadow.style.opacity = 1f;
			shadow.pickingMode = PickingMode.Ignore;

			overlayRoot.Add(shadow);
			overlayRoot.Add(label);
			root.Add(overlayRoot);
		}

		private static void UpdateOverlayText(EditorWindow win)
		{
			VisualElement root = win.rootVisualElement;
			if (root == null)
				return;

			var label = root.Q<Label>(OverlayLabelName);
			var shadow = root.Q<Label>(OverlayShadowLabelName);
			if (label == null)
				return;

			if (!TryBuildStatsString(out string text))
			{
				label.text = string.Empty;
				if (shadow != null) shadow.text = string.Empty;
				return;
			}

			label.text = text;
			if (shadow != null) shadow.text = text;
		}

		private static void ForceRefreshAllKnownWindows()
		{
			var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
			for (int i = 0; i < windows.Length; i++)
			{
				if (IsUltimatePreviewWindow(windows[i]))
					windows[i].Repaint();
			}
		}

		private static bool TryBuildStatsString(out string text)
		{
			text = string.Empty;

			Mesh mesh = null;
			Renderer foundRenderer = null;

			UnityEngine.Object active = Selection.activeObject;
			if (active == null)
				return false;

			if (active is Mesh meshAsset)
			{
				mesh = meshAsset;
			}
			else if (active is GameObject go)
			{
				if (go.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
				{
					mesh = mf.sharedMesh;
					foundRenderer = go.GetComponent<Renderer>();
				}
				else if (go.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh != null)
				{
					mesh = smr.sharedMesh;
					foundRenderer = smr;
				}
			}
			else if (active is Component comp)
			{
				if (comp.TryGetComponent<MeshFilter>(out var mf2) && mf2.sharedMesh != null)
				{
					mesh = mf2.sharedMesh;
					foundRenderer = comp.GetComponent<Renderer>();
				}
				else if (comp is SkinnedMeshRenderer smr2 && smr2.sharedMesh != null)
				{
					mesh = smr2.sharedMesh;
					foundRenderer = smr2;
				}
			}

			if (mesh == null)
				return false;

			int vertexCount = mesh.vertexCount;
			int triangleCount = 0;
			int subMeshCount = Mathf.Max(mesh.subMeshCount, 1);
			for (int i = 0; i < subMeshCount; i++)
			{
				try
				{
					triangleCount += (int)(mesh.GetIndexCount(i) / 3UL);
				}
				catch
				{
					// Fallback for meshes without submeshes
					triangleCount = mesh.triangles != null ? mesh.triangles.Length / 3 : triangleCount;
					break;
				}
			}

			// Determine LOD index if applicable
			int lodIndex = 0;
			if (foundRenderer != null)
			{
				var lodGroup = foundRenderer.GetComponentInParent<LODGroup>();
				if (lodGroup != null)
				{
					var lods = lodGroup.GetLODs();
					for (int i = 0; i < lods.Length; i++)
					{
						if (lods[i].renderers != null && lods[i].renderers.Contains(foundRenderer))
						{
							lodIndex = i;
							break;
						}
					}
				}
			}

			text = $"Mesh LOD {lodIndex} - {vertexCount:N0} Vertices, {triangleCount:N0} Triangles";
			return true;
		}
	}
}
#endif


