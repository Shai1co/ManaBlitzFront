
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
			var move = config.GetDefaultMove(unit.PieceId);
			if (move == null || move.patterns == null || move.patterns.Count == 0) return set;

			var origin = unit.CurrentPosition;
			foreach (var pattern in move.patterns)
			{
				ApplyPattern(origin, unit.OwnerId, pattern, move.overFriendly, move.overEnemy, set);
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

		private static void ApplyPattern(Vector2Int origin, string ownerId, UnitConfig.MovePattern pattern, bool overFriendly, bool overEnemy, HashSet<Vector2Int> result)
		{
			if (pattern == null) return;
			var dirs = GetDirections(pattern.dir, ownerId);
			foreach (var dir in dirs)
			{
				TraceInDirection(origin, dir, pattern.rangeMin, pattern.rangeMax, pattern.leap, pattern.stopOnHit, overFriendly, overEnemy, result);
			}
		}

		private static List<Vector2Int> GetDirections(string dir, string ownerId)
		{
			// orthogonal, diagonal, forward (relative to owner), any (BFS in all 8 directions)
			var list = new List<Vector2Int>();
			switch (dir)
			{
				case "orthogonal":
					list.Add(new Vector2Int(1, 0)); list.Add(new Vector2Int(-1, 0)); list.Add(new Vector2Int(0, 1)); list.Add(new Vector2Int(0, -1));
					break;
				case "diagonal":
					list.Add(new Vector2Int(1, 1)); list.Add(new Vector2Int(-1, -1)); list.Add(new Vector2Int(1, -1)); list.Add(new Vector2Int(-1, 1));
					break;
				case "forward":
					// Define forward as +y for owner, -y for enemy owner (simple heuristic)
					bool isOwn = AuthManager.Instance != null && string.Equals(ownerId, AuthManager.Instance.UserId);
					list.Add(isOwn ? new Vector2Int(0, 1) : new Vector2Int(0, -1));
					break;
				case "any":
					list.Add(new Vector2Int(1, 0)); list.Add(new Vector2Int(-1, 0)); list.Add(new Vector2Int(0, 1)); list.Add(new Vector2Int(0, -1));
					list.Add(new Vector2Int(1, 1)); list.Add(new Vector2Int(-1, -1)); list.Add(new Vector2Int(1, -1)); list.Add(new Vector2Int(-1, 1));
					break;
				default:
					break;
			}
			return list;
		}

		private static void TraceInDirection(Vector2Int origin, Vector2Int step, int minRange, int maxRange, bool leap, bool stopOnHit, bool overFriendly, bool overEnemy, HashSet<Vector2Int> result)
		{
			if (maxRange < 1) return;
			var current = origin;
			for (int dist = 1; dist <= maxRange; dist++)
			{
				current = new Vector2Int(current.x + step.x, current.y + step.y);
				if (!Board.Instance.TryGetSlot(current, out var slot)) break;
				var bs = slot.GetComponent<BoardSlot>();
				bool occupied = bs != null && bs.IsOccupied;
				if (dist >= minRange)
				{
					if (!occupied)
					{
						result.Add(current);
					}
					else
					{
						// Can traverse over units but cannot land on them for movement
						if ((overFriendly || overEnemy))
						{
							// do not add occupied cell as a landing spot
						}
					}
				}
				if (occupied && !leap)
				{
					if (stopOnHit) break;
					else break; // movement cannot pass through unless leap; passThrough not supported for character default move
				}
			}
		}
	}
}
