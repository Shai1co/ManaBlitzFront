
using System;
using GameDevWare.Serialization;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace ManaGambit
{
	[Serializable]
	public class Pos
	{
		public int x;
		public int y;
	}

	[Serializable]
	public class UnitServerData
	{
		public string unitId;
		public string pieceId;
		public string ownerId;
		public Pos pos;
		public int hp;
		public int maxHp;
		public float mana;
		/// <summary>
		/// Animation state from server: "Idle", "Moving", "Attacking", "WindUp"
		/// If null/empty, defaults to "Idle"
		/// </summary>
		public string animState;
	}

	[Serializable]
	public class BoardData
	{
		public int width;
		public int height;
		public UnitServerData[] units;
	}

	[Serializable]
	public class GameData
	{
		public int startTick;
		public BoardData board;
		public Dictionary<string, float> playerMana;
		/// <summary>
		/// Player names keyed by playerId. Optional field for future server support.
		/// </summary>
		public Dictionary<string, string> playerNames;
	}

	[Serializable]
	public class GameEvent
	{
		public string type;
		public int serverTick;
		public GameData data;
	}

	[Serializable]
	public class MoveData
	{
		public string unitId;
		public Pos from;
		public Pos to;
		public int startTick;
		public int endTick;
		public int currentPips; // optional; piggybacked for mover
		public string intentId; // added for complete logging
		public int serverTick; // added for complete logging
		/// <summary>
		/// Animation state during/after movement: "Idle", "Moving", "Attacking", "WindUp"
		/// If null/empty, defaults based on movement context
		/// </summary>
		public string animState;
		/// <summary>
		/// Movement reason: "Approach", "PostImpact", "Swap", or null for regular moves
		/// </summary>
		public string reason;
		/// <summary>
		/// Movement style: "Teleport" for instant movement, or null for normal movement
		/// </summary>
		public string moveStyle;
	}

	[Serializable]
	public class MoveEvent
	{
		public string type;
		public string intentId;
		public int serverTick;
		public MoveData data;
	}

	[Serializable]
	public class UseSkillData
	{
		public string unitId;
		public int skillId;
		public Pos origin;
		public SkillTarget target;
		public int startTick;
		public int endWindupTick;
		public int hitTick;
		public int currentPips; // optional; piggybacked for attacker
		public string intentId; // added for complete logging
		public int serverTick; // added for complete logging
	}

	[Serializable]
	public class SkillTarget
	{
		public Pos cell;
		public string unitId;
	}

	[Serializable]
	public class UseSkillEvent
	{
		public string type;
		public string intentId;
		public int serverTick;
		public UseSkillData data;
	}

	[Serializable]
	public class UseSkillResultTarget
	{
		public string unitId;
		public int x; // target coordinates (added to match JS client)
		public int y; // target coordinates (added to match JS client)
		public int result;
		public int damage;
		public bool killed;
		public bool crit; // critical hit flag (added to match JS client)
		public int hp; // optional; server may include updated hp
		public bool dead; // optional alias used by server
	}

	[Serializable]
	public class ClientTarget
	{
		public Pos cell;
	}

	[Serializable]
	public class UseSkillResultData
	{
		public int skillId;
		public string attacker;
		public int hitTick;
		public string intentId; // added to match JS client logging
		public string damageType; // added to match JS client (e.g., "physical", "magic")
		public UseSkillResultTarget[] targets;
		public ClientTarget clientTarget; // added to match JS client
		public int serverTick; // added to match JS client logging
		public int currentPips; // optional; piggybacked for attacker
	}

	[Serializable]
	public class UseSkillResultEvent
	{
		public string type;
		public string intentId;
		public int serverTick;
		public UseSkillResultData data;
	}

	[Serializable]
	public class StatusChange
	{
		public string name;
		public string op;
		public int startTick;
		public int endTick;
	}

	[Serializable]
	public class StatusUpdateData
	{
		public string unitId;
		public StatusChange[] changes;
	}

	[Serializable]
	public class StatusUpdateEvent
	{
		public string type;
		public int serverTick;
		public StatusUpdateData data;
	}

	[Serializable]
	public class GameOverData
	{
		public int reason;
		public string winnerUserId;
	}

	[Serializable]
	public class GameOverEvent
	{
		public string type;
		public int serverTick;
		public GameOverData data;
	}

	[Serializable]
	public class ErrorData
	{
		public string code;
		public string msg;
		public string iid;
		public bool retry;
	}

	[Serializable]
	public class ErrorEvent
	{
		public string type;
		public int serverTick;
		public ErrorData data;
	}

	// Intent envelope DTOs for sending
	[Serializable]
	public class IntentEnvelope<TPayload>
	{
		public string intentId;
		public string userId;
		public string matchId;
		public string name; // Move | UseSkill | CancelCast | LeaveMatch
		public TPayload payload;
		public long clientTick;
		public long clientTs;
	}

	[Serializable]
	public class MovePayload
	{
		public string unitId;
		public Pos from;
		public Pos to;
	}

	[Serializable]
	public class UseSkillPayload
	{
		public string unitId;
		public int skillId;
		public SkillTarget target;
		public string skillName;
		public PostImpact postImpact; // optional; used for landing behavior on move skills
	}

	[Serializable]
	public class CancelCastPayload
	{
		public string unitId;
		public string castId;
	}

	[Serializable]
	public class LeaveMatchPayload
	{
		public string reason;
	}

	[Serializable]
	public class PostImpact
	{
		public string behavior; // ReturnHome | LandNearTarget
		public int radius;
		public bool random;
	}

	[Serializable]
	public class Countdown
	{
		public int startsAtTick;
		public int countdownTicks;
	}

	[Serializable]
	public class StateSnapshot
	{
		public string matchId;
		public int serverTick;
		public int startTick;
		public BoardData board;
		/// <summary>
		/// Mana per player keyed by playerId (not userId).
		/// </summary>
		public Dictionary<string, float> playerMana = new Dictionary<string, float>();
		/// <summary>
		/// Player names keyed by playerId. Optional field for future server support.
		/// </summary>
		public Dictionary<string, string> playerNames;
		// NOTE: server currently sends 'countdown' as a string token instead of an object
		// Using loose-typed field to accept both string and object tokens
		public object countdown;

		/// <summary>
		/// Attempts to safely convert the countdown field to a Countdown instance.
		/// Handles both string tokens (ignores them) and object tokens (deserializes to Countdown).
		/// </summary>
		/// <param name="countdown">The resulting Countdown instance if conversion succeeds</param>
		/// <returns>True if a valid Countdown was extracted, false otherwise</returns>
		public bool TryGetCountdown(out Countdown countdown)
		{
			countdown = null;
			
			if (this.countdown == null)
				return false;

			// If it's already a Countdown object, return it directly
			if (this.countdown is Countdown existingCountdown)
			{
				countdown = existingCountdown;
				return true;
			}

			// If it's a JToken, handle based on its type
			if (this.countdown is JToken token)
			{
				switch (token.Type)
				{
					case JTokenType.String:
						// String tokens are ignored (as per the original comment)
						return false;
					case JTokenType.Object:
						try
						{
							countdown = token.ToObject<Countdown>();
							return countdown != null;
						}
						catch
						{
							return false;
						}
					default:
						return false;
				}
			}

			// If it's a plain object, try to convert it
			try
			{
				if (this.countdown is string)
				{
					// String values are ignored
					return false;
				}
				
				// Try to convert using JsonConvert
				var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(this.countdown);
				countdown = Newtonsoft.Json.JsonConvert.DeserializeObject<Countdown>(jsonString);
				return countdown != null;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Gets the countdown value if available, otherwise returns null.
		/// This is a simpler accessor that doesn't throw exceptions.
		/// </summary>
		/// <returns>The Countdown instance if valid, null otherwise</returns>
		public Countdown GetCountdown()
		{
			TryGetCountdown(out Countdown result);
			return result;
		}
	}

	[Serializable]
	public class RequestValidTargets
	{
		public string unitId;
		public string name; // "Move" | "UseSkill"
		public int skillId; // optional; when name=="UseSkill" provide selected skill index, else -1
	}

	[Serializable]
	public class TargetCell
	{
		public Pos cell;
	}

	[Serializable]
	public class ValidTargetsData
	{
		public string unitId;
		public TargetCell[] targets;
	}

	[Serializable]
	public class ValidTargetsEvent
	{
		public string type;
		public int serverTick;
		public ValidTargetsData data;
	}

	[Serializable]
	public class ServerEventEnvelopeRaw
	{
		public string type;
		public string intentId;
		public int serverTick;
		public IndexedDictionary<string, object> data;
	}

	[Serializable]
	public class ManaUpdateData
	{
		public string playerId;
		public float mana;
	}

	[Serializable]
	public class ManaUpdateEvent
	{
		public string type;
		public int serverTick;
		public ManaUpdateData data;
	}

	[Serializable]
	public class UnitDiedData
	{
		public string unitId;
		public string killerId; // optional; the unit that caused the death
		public int deathTick;
	}

	[Serializable]
	public class UnitDiedEvent
	{
		public string type;
		public int serverTick;
		public UnitDiedData data;
	}
}
