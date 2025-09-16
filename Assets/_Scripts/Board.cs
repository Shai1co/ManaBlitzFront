
using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using System.Text.RegularExpressions;

namespace ManaGambit
{
	public class Board : MonoBehaviour
	{
		public static Board Instance { get; private set; }

		public const int Size = 8;
		private const float TileSize = 1f;

		[SerializeField] private Vector3 origin = Vector3.zero;
		[SerializeField] private Transform boardContainer;
		[ShowInInspector, ReadOnly]
		private readonly Dictionary<Vector2Int, Transform> coordToSlot = new Dictionary<Vector2Int, Transform>();
		private readonly Dictionary<Transform, Vector2Int> slotToCoord = new Dictionary<Transform, Vector2Int>();
		private readonly Dictionary<Unit, Vector2Int> unitToCoord = new Dictionary<Unit, Vector2Int>();
		[ShowInInspector, ReadOnly]
		private readonly Dictionary<Vector2Int, BoardSlot> coordToBoardSlot = new Dictionary<Vector2Int, BoardSlot>();

		public IEnumerable<KeyValuePair<Vector2Int, Transform>> GetAllSlots()
		{
			return coordToSlot;
		}

		public void SetOccupied(Vector2Int coord, Unit unit)
		{
			if (coordToSlot.TryGetValue(coord, out var slot))
			{
				var bs = slot.GetComponent<BoardSlot>();
				if (bs != null) bs.SetOccupant(unit);
			}
			unitToCoord[unit] = coord;
		}

		public void ClearOccupied(Vector2Int coord, Unit unit)
		{
			if (coordToSlot.TryGetValue(coord, out var slot))
			{
				var bs = slot.GetComponent<BoardSlot>();
				if (bs != null) bs.ClearOccupant(unit);
			}
			if (unitToCoord.TryGetValue(unit, out var current) && current == coord)
			{
				unitToCoord.Remove(unit);
			}
		}

		public void ShowAvailableHighlights()
		{
			foreach (var kv in coordToSlot)
			{
				var slot = kv.Value;
				var bs = slot != null ? slot.GetComponent<BoardSlot>() : null;
				if (bs == null) continue;
				if (bs.IsOccupied) bs.HideHighlight(); else bs.ShowHighlight();
			}
		}

		public void HideAllHighlights()
		{
			foreach (var kv in coordToSlot)
			{
				var slot = kv.Value;
				var bs = slot != null ? slot.GetComponent<BoardSlot>() : null;
				if (bs == null) continue;
				bs.HideHighlight();
				bs.HideSkillHighlight();
			}
		}

		public void ClearMoveHighlights()
		{
			foreach (var kv in coordToSlot)
			{
				var bs = kv.Value != null ? kv.Value.GetComponent<BoardSlot>() : null;
				if (bs == null) continue;
				bs.HideHighlight();
			}
		}

		public void ClearSkillHighlights()
		{
			foreach (var kv in coordToSlot)
			{
				var bs = kv.Value != null ? kv.Value.GetComponent<BoardSlot>() : null;
				if (bs == null) continue;
				bs.HideSkillHighlight();
			}
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			BuildSlotsFromContainer();
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void EditorAutoInit()
		{
			EditorEnsureInstance();
		}

		public static void EditorEnsureInstance()
		{
			if (Instance == null)
			{
				var found = UnityEngine.Object.FindFirstObjectByType<Board>(UnityEngine.FindObjectsInactive.Include);
				if (found != null)
				{
					Instance = found;
				}
			}
		}
		/// <summary>
		/// Ensure slot maps are built when used from editor tools (Awake may not have run).
		/// </summary>
		public void EditorEnsureBuilt()
		{
			// Rebuild if we have no cached slots
			if (coordToSlot == null || coordToSlot.Count == 0)
			{
				BuildSlotsFromContainer();
			}
		}
#endif
		[Button]
		private void BuildSlotsFromContainer()
		{
			coordToSlot.Clear();
			slotToCoord.Clear();
			if (boardContainer == null)
			{
				boardContainer = transform;
				if (boardContainer == null)
				{
					Debug.LogWarning("Board: boardContainer is null; cannot build slots.");
					return;
				}
			}

			var all = boardContainer.GetComponentsInChildren<Transform>(true);
			int added = 0;
			for (int i = 0; i < all.Length; i++)
			{
				var child = all[i];
				if (child == null || child == boardContainer) continue;
				// Try to extract two integers from the name (supports prefixes/suffixes like "Tile_0_0")
				if (!TryExtractRowCol(child.name, out int row, out int col)) continue;
				var coord = new Vector2Int(col, row); // first number is row (y), second is column (x)
				if (!coordToSlot.ContainsKey(coord))
				{
					coordToSlot.Add(coord, child);
					added++;
				}
				if (!slotToCoord.ContainsKey(child))
				{
					slotToCoord.Add(child, coord);
				}
				var boardSlot = child.GetComponent<BoardSlot>();
				if (boardSlot == null)
				{
					boardSlot = child.gameObject.AddComponent<BoardSlot>();
				}
				boardSlot.EnsureSetup();
				coordToBoardSlot[coord] = boardSlot;
			}
			Debug.Log($"Built {coordToSlot.Count} slots from container (added {added})");
		}

		private static bool TryExtractRowCol(string name, out int row, out int col)
		{
			row = 0; col = 0;
			if (string.IsNullOrEmpty(name)) return false;
			var matches = Regex.Matches(name, "-?\\d+");
			if (matches == null || matches.Count < 2) return false;
			// Use the last two integers in the name
			if (!int.TryParse(matches[matches.Count - 2].Value, out row)) return false;
			if (!int.TryParse(matches[matches.Count - 1].Value, out col)) return false;
			return true;
		}

		public bool TryGetSlot(Vector2Int coord, out Transform slot)
		{
			return coordToSlot.TryGetValue(coord, out slot);
		}

		public bool TryGetCoord(Transform slot, out Vector2Int coord)
		{
			return slotToCoord.TryGetValue(slot, out coord);
		}

		public bool TryGetCoord(Unit unit, out Vector2Int coord)
		{
			return unitToCoord.TryGetValue(unit, out coord);
		}

		public Vector3 GetSlotWorldPosition(Vector2Int coord)
		{
			if (coordToSlot.TryGetValue(coord, out var slot))
			{
				return slot.position;
			}
			// Fallback to grid origin math if slot not found
			return origin + new Vector3(coord.x * TileSize, 0, coord.y * TileSize);
		}

		public bool TryGetBoardSlot(Vector2Int coord, out BoardSlot slot)
		{
			return coordToBoardSlot.TryGetValue(coord, out slot);
		}

		public void HighlightSlot(Vector2Int coord, bool visible)
		{
			if (coordToBoardSlot.TryGetValue(coord, out var boardSlot))
			{
				boardSlot.SetHighlightVisible(visible);
			}
		}

		public void HighlightSlots(IEnumerable<Vector2Int> coords, bool visible)
		{
			if (coords == null) return;
			foreach (var coord in coords)
			{
				HighlightSlot(coord, visible);
			}
		}

		public void HighlightSkillSlots(IEnumerable<Vector2Int> coords, bool visible)
		{
			if (coords == null) return;
			foreach (var coord in coords)
			{
				if (coordToBoardSlot.TryGetValue(coord, out var boardSlot))
				{
					boardSlot.SetSkillHighlightVisible(visible);
				}
			}
		}

		public Vector3 GetWorldPosition(Vector2Int coord)
		{
			if (coord.x < 0 || coord.x >= Size || coord.y < 0 || coord.y >= Size)
			{
				throw new System.ArgumentOutOfRangeException(nameof(coord), "Coordinate must be within board bounds (0 to 7)");
			}

			return origin + new Vector3(coord.x * TileSize, 0, coord.y * TileSize);
		}

		public Vector2Int GetCoordinateFromWorld(Vector3 worldPos)
		{
			Vector3 localPos = worldPos - origin;
			int x = Mathf.FloorToInt(localPos.x / TileSize);
			int y = Mathf.FloorToInt(localPos.z / TileSize);
			return new Vector2Int(x, y);
		}

		public bool IsOccupied(Vector2Int coord)
		{
			if (coordToBoardSlot.TryGetValue(coord, out var boardSlot))
			{
				return boardSlot.IsOccupied;
			}
			return false;
		}
	}
}
