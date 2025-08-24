
using System;

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
		public int result;
		public int damage;
		public bool killed;
	}

	[Serializable]
	public class UseSkillResultData
	{
		public int skillId;
		public string attacker;
		public int hitTick;
		public UseSkillResultTarget[] targets;
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
		// public Dictionary<string, float> playerMana; // not used now
		public Countdown countdown;
	}

	[Serializable]
	public class RequestValidTargets
	{
		public string unitId;
		public string name; // "Move" | "UseSkill"
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
}
