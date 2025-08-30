
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
			// Deprecated placeholder; route to ComputeSkillTargets for full pattern logic
			return ComputeSkillTargets(unit, config, skillIndex);
		}
		[System.Obsolete("Use ComputeSkillTargets(unit, config, skillIndex) instead.")]
		public static void ComputeAttackTargets_ObsoleteNotice() {}

		public static HashSet<Vector2Int> ComputeSkillTargets(Unit unit, UnitConfig config, int skillIndex = 0)
		{
			var result = new HashSet<Vector2Int>();
			if (unit == null || config == null) return result;
			var data = config.GetData(unit.PieceId);
			if (data == null || data.actions == null || data.actions.Length == 0) return result;
			if (skillIndex < 0 || skillIndex >= data.actions.Length) return result;
			var action = data.actions[skillIndex];
			if (action == null) return result;

			// Attack patterns
			if (action.attack != null && action.attack.patterns != null && action.attack.patterns.Count > 0)
			{
				for (int i = 0; i < action.attack.patterns.Count; i++)
				{
					var p = action.attack.patterns[i];
					TraceForAttack(unit, p,
						passThrough: p.passThrough,
						overFriendly: action.attack.overFriendly,
						overEnemy: action.attack.overEnemy,
						stopOnHit: action.attack.stopOnHit,
						pierce: action.attack.pierce,
						allowFriendlyTarget: action.attack.allowFriendlyTarget, // Use attack configuration flag, defaults to false if not set
						collect: result);
				}
			}

			// Move skill patterns (land on empty tiles like movement)
			if (action.move != null && action.move.patterns != null && action.move.patterns.Count > 0)
			{
				for (int i = 0; i < action.move.patterns.Count; i++)
				{
					var p = action.move.patterns[i];
					TraceForMoveSkill(unit, p,
						overFriendly: action.move.overFriendly,
						overEnemy: action.move.overEnemy,
						collect: result);
				}
			}

			// Swap: targets must be occupied cells with allowed allegiance
			if (action.swap != null && action.swap.patterns != null && action.swap.patterns.Count > 0)
			{
				for (int i = 0; i < action.swap.patterns.Count; i++)
				{
					var p = action.swap.patterns[i];
					TraceForSwap(unit, p, targetFriendly: action.swap.targetFriendly, overFriendly: action.swap.overFriendly, overEnemy: action.swap.overEnemy, collect: result);
				}
			}

			// Buff: simple self-target or team targets if specified
			if (action.buff != null)
			{
				var t = action.buff.targets;
				if (t == "self")
				{
					result.Add(unit.CurrentPosition);
				}
				else if (t == "team")
				{
					// Highlight all friendly unit positions
					foreach (var kv in Board.Instance.GetAllSlots())
					{
						if (Board.Instance.TryGetBoardSlot(kv.Key, out var bs) && bs != null && bs.IsOccupied && bs.Occupant != null)
						{
							// Team targets are determined by comparing the occupant's OwnerId to the unit's OwnerId (consistent with other ownership checks)
							bool isFriendly = string.Equals(bs.Occupant.OwnerId, unit.OwnerId);
							if (isFriendly) result.Add(kv.Key);
						}
					}
				}
			}
			// Aura: allow choosing a center within radius (simple diamond/Manhattan)
			if (action.aura != null && action.aura.radius > 0)
			{
				var origin = unit.CurrentPosition;
				int r = action.aura.radius;
				for (int dx = -r; dx <= r; dx++)
				{
					for (int dy = -r; dy <= r; dy++)
					{
						int md = Mathf.Abs(dx) + Mathf.Abs(dy);
						if (md <= r)
						{
							var c = new Vector2Int(origin.x + dx, origin.y + dy);
							if (Board.Instance.TryGetSlot(c, out var _)) result.Add(c);
						}
					}
				}
			}

			return result;
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

		private static void TraceForAttack(Unit unit, UnitConfig.MovePattern pattern, bool passThrough, bool overFriendly, bool overEnemy, bool stopOnHit, int pierce, bool allowFriendlyTarget, HashSet<Vector2Int> collect)
		{
			if (pattern == null) return;
			var origin = unit.CurrentPosition;
			int minRange = Mathf.Max(0, pattern.rangeMin);
			int maxRange = Mathf.Max(minRange, pattern.rangeMax);
			// 'any' uses BFS similar to JS client
			if (string.Equals(pattern.dir, "any"))
			{
				// Track best remaining pierce seen per cell to allow re-entry when we have more remaining pierce
				var bestRemainingPierceAt = new Dictionary<Vector2Int, int>(64);
				int initialRemainingPierce = (pierce < 0) ? int.MaxValue : pierce;
				var q = new Queue<(Vector2Int pos, int steps, bool stopped, int remainingPierce)>();
				q.Enqueue((origin, 0, false, initialRemainingPierce));
				bestRemainingPierceAt[origin] = initialRemainingPierce;
				while (q.Count > 0)
				{
					var (cur, steps, stopped, remainingPierce) = q.Dequeue();
					if (steps > maxRange) continue;
					if (steps >= minRange)
					{
						if (cur != origin)
						{
							if (Board.Instance.TryGetBoardSlot(cur, out var bs) && bs.IsOccupied && bs.Occupant != null)
							{
								bool isFriendlyHere = AuthManager.Instance != null && string.Equals(bs.Occupant.OwnerId, AuthManager.Instance.UserId);
								bool canCollectHere = (isFriendlyHere && overFriendly) || (!isFriendlyHere && overEnemy) || allowFriendlyTarget;
								if (canCollectHere)
								{
									collect.Add(cur);
								}
							}
						}
					}
					if (steps == maxRange || stopped) continue;
					// Explore 8-neighborhood
					for (int dx = -1; dx <= 1; dx++)
					{
						for (int dy = -1; dy <= 1; dy++)
						{
							if (dx == 0 && dy == 0) continue;
							var nx = new Vector2Int(cur.x + dx, cur.y + dy);
							if (!Board.Instance.TryGetSlot(nx, out var _)) continue;
							// Determine occupancy and friendliness
							bool occ = Board.Instance.TryGetBoardSlot(nx, out var nbs) && nbs.IsOccupied && nbs.Occupant != null;
							bool isFriendlyNext = occ && (AuthManager.Instance != null && string.Equals(nbs.Occupant.OwnerId, AuthManager.Instance.UserId));
							int nextRemainingPierce = remainingPierce;
							bool nextStopped = false;

							if (occ)
							{
								if (!passThrough)
								{
									bool canPassOver = isFriendlyNext ? overFriendly : overEnemy;
									if (!canPassOver)
									{
										// Need to consume pierce to continue past this unit
										nextRemainingPierce = (remainingPierce == int.MaxValue) ? int.MaxValue : remainingPierce - 1;
										// If we consumed a hit and stopOnHit is set, do not expand neighbors from this node
										if (stopOnHit)
										{
											nextStopped = true;
										}
									}
								}
								// When remaining pierce dropped below 0 due to consumption, we can still enqueue as a leaf to allow collection, but cannot expand
								if (nextRemainingPierce < 0)
								{
									nextStopped = true;
								}
							}

							// Respect best remaining pierce per cell to avoid revisiting with worse or equal pierce
							if (bestRemainingPierceAt.TryGetValue(nx, out var bestRem) && bestRem >= nextRemainingPierce) continue;
							bestRemainingPierceAt[nx] = nextRemainingPierce;
							q.Enqueue((nx, steps + 1, nextStopped, nextRemainingPierce));
						}
					}
				}
				return;
			}

			// Directional rays with pierce/stop logic
			var dirs = GetDirections(pattern.dir, unit.OwnerId);
			for (int d = 0; d < dirs.Count; d++)
			{
				var step = dirs[d];
				int pierced = 0;
				for (int dist = 1; dist <= Mathf.Max(1, maxRange); dist++)
				{
					var cur = new Vector2Int(origin.x + step.x * dist, origin.y + step.y * dist);
					if (!Board.Instance.TryGetSlot(cur, out var slot)) break;
					var bs = slot.GetComponent<BoardSlot>();
					bool occupied = bs != null && bs.IsOccupied && bs.Occupant != null;
					if (dist >= minRange && occupied)
					{
						bool isFriendly = AuthManager.Instance != null && string.Equals(bs.Occupant.OwnerId, AuthManager.Instance.UserId);
						if (!isFriendly || allowFriendlyTarget) collect.Add(cur);
					}
					if (occupied && !passThrough)
					{
						bool canPass = (AuthManager.Instance != null && bs.Occupant != null && string.Equals(bs.Occupant.OwnerId, AuthManager.Instance.UserId)) ? overFriendly : overEnemy;
						if (!canPass)
						{
							if (stopOnHit) break;
							pierced++;
							if (pierce >= 0 && pierced > pierce) break;
						}
					}
				}
			}
		}

		private static void TraceForMoveSkill(Unit unit, UnitConfig.MovePattern pattern, bool overFriendly, bool overEnemy, HashSet<Vector2Int> collect)
		{
			if (pattern == null) return;
			var origin = unit.CurrentPosition;
			int minRange = Mathf.Max(0, pattern.rangeMin);
			int maxRange = Mathf.Max(minRange, pattern.rangeMax);
			if (string.Equals(pattern.dir, "any"))
			{
				var visited = new Dictionary<Vector2Int, int>(64);
				var q = new Queue<(Vector2Int pos, int steps)>();
				q.Enqueue((origin, 0));
				visited[origin] = 0;
				while (q.Count > 0)
				{
					var (cur, steps) = q.Dequeue();
					if (steps > maxRange) continue;
					if (steps >= minRange && cur != origin)
					{
						// Land only on empty tiles
						bool occ = Board.Instance.TryGetBoardSlot(cur, out var bs) && bs.IsOccupied;
						if (!occ) collect.Add(cur);
					}
					if (steps == maxRange) continue;
					for (int dx = -1; dx <= 1; dx++)
					{
						for (int dy = -1; dy <= 1; dy++)
						{
							if (dx == 0 && dy == 0) continue;
							var nx = new Vector2Int(cur.x + dx, cur.y + dy);
							if (!Board.Instance.TryGetSlot(nx, out var _)) continue;
							if (visited.TryGetValue(nx, out var prev) && prev <= steps + 1) continue;
							bool occ = Board.Instance.TryGetBoardSlot(nx, out var nbs) && nbs.IsOccupied && nbs.Occupant != null;
							if (occ)
							{
								bool isFriendly = AuthManager.Instance != null && string.Equals(nbs.Occupant.OwnerId, AuthManager.Instance.UserId);
								bool canTraverse = isFriendly ? overFriendly : overEnemy;
								if (!canTraverse) continue;
							}
							visited[nx] = steps + 1;
							q.Enqueue((nx, steps + 1));
						}
					}
				}
				return;
			}

			// Directional rays for movement-like skills (land on empty tiles only)
			var dirs = GetDirections(pattern.dir, unit.OwnerId);
			for (int d = 0; d < dirs.Count; d++)
			{
				var step = dirs[d];
				for (int dist = 1; dist <= Mathf.Max(1, maxRange); dist++)
				{
					var cur = new Vector2Int(origin.x + step.x * dist, origin.y + step.y * dist);
					if (!Board.Instance.TryGetSlot(cur, out var slot)) break;
					if (!Board.Instance.TryGetBoardSlot(cur, out var bs)) break;
					bool occupied = bs.IsOccupied && bs.Occupant != null;
					if (occupied)
					{
						bool isFriendly = AuthManager.Instance != null && string.Equals(bs.Occupant.OwnerId, AuthManager.Instance.UserId);
						bool canTraverse = isFriendly ? overFriendly : overEnemy;
						if (!canTraverse) break;
						// can traverse, but cannot land here
						continue;
					}
					if (dist >= minRange)
					{
						collect.Add(cur);
					}
				}
			}
		}

        private static void TraceForSwap(Unit unit, UnitConfig.MovePattern pattern, bool targetFriendly, bool overFriendly, bool overEnemy, HashSet<Vector2Int> collect)
        {
            if (pattern == null) return;
            var origin = unit.CurrentPosition;
            var dirs = GetDirections(pattern.dir, unit.OwnerId);
            for (int d = 0; d < dirs.Count; d++)
            {
                var step = dirs[d];
                var current = origin;
                // Respect minimum range before collecting targets
                int minRange = Mathf.Max(1, pattern.rangeMin);
                int maxRange = Mathf.Max(minRange, pattern.rangeMax);
                for (int dist = 1; dist <= maxRange; dist++)
                {
                    current = new Vector2Int(current.x + step.x, current.y + step.y);
                    if (!Board.Instance.TryGetBoardSlot(current, out var bs)) break;
                    bool occupied = bs.IsOccupied && bs.Occupant != null;
                    if (occupied)
                    {
                        bool isFriendly = bs.Occupant.OwnerId == unit.OwnerId;
                        // Only collect if we've reached or passed the minimum range
                        if (dist >= minRange && ((targetFriendly && isFriendly) || (!targetFriendly && !isFriendly)))
                        {
                            collect.Add(current);
                            break; // can swap at first encountered valid unit
                        }
                        // still allow traversal if passing over is permitted
                        bool canPass = isFriendly ? overFriendly : overEnemy;
                        if (!canPass) break;
                    }
                }
            }
        }	}
}
