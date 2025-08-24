
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace ManaGambit
{
	public static class LocalTargeting
	{
		public static HashSet<Vector2Int> ComputeMoveTargets(Unit unit, UnitConfig config)
		{
			var set = new HashSet<Vector2Int>();
			if (unit == null || config == null) return set;
			var data = config.GetData(unit.PieceId);
			if (data == null) return set;
			// Find move patterns in characters.json via UnitConfig (not fully exposed). For now, allow 1-step any dir (temporary) if not found.
			// TODO: Expand UnitConfig to expose move.patterns if needed. Temporary: show all empty slots (already implemented elsewhere).
			foreach (var kv in Board.Instance.GetAllSlots())
			{
				var coord = kv.Key;
				if (coord == unit.CurrentPosition) continue;
				if (Board.Instance.TryGetSlot(coord, out var t))
				{
					var bs = t.GetComponent<BoardSlot>();
					if (bs != null && !bs.IsOccupied)
					{
						set.Add(coord);
					}
				}
			}
			return set;
		}

		public static HashSet<Vector2Int> ComputeAttackTargets(Unit unit, UnitConfig config, int skillIndex = 0)
		{
			var set = new HashSet<Vector2Int>();
			if (unit == null || config == null) return set;
			var data = config.GetData(unit.PieceId);
			if (data == null || data.actions == null || data.actions.Length == 0) return set;
			// Temporary: adjacent cells as a placeholder (server authoritative checks still apply)
			var pos = unit.CurrentPosition;
			var candidates = new Vector2Int[]
			{
				new Vector2Int(pos.x+1, pos.y),
				new Vector2Int(pos.x-1, pos.y),
				new Vector2Int(pos.x, pos.y+1),
				new Vector2Int(pos.x, pos.y-1),
				new Vector2Int(pos.x+1, pos.y+1),
				new Vector2Int(pos.x-1, pos.y-1),
				new Vector2Int(pos.x+1, pos.y-1),
				new Vector2Int(pos.x-1, pos.y+1),
			};
			foreach (var c in candidates)
			{
				if (Board.Instance.TryGetSlot(c, out var tr))
				{
					set.Add(c);
				}
			}
			return set;
		}
	}
}
