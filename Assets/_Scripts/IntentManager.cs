
using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
	public class IntentManager : MonoBehaviour
	{
		public static IntentManager Instance { get; private set; }

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		public async UniTask SendMoveIntent(string unitId, Pos from, Pos to)
		{
			var intent = new IntentEnvelope<MovePayload>
			{
				intentId = Guid.NewGuid().ToString(),
				userId = AuthManager.Instance.UserId,
				matchId = NetworkManager.Instance.MatchId,
				name = "Move",
				payload = new MovePayload { unitId = unitId, from = from, to = to },
				clientTick = GetCurrentClientTick(),
				clientTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};

			await NetworkManager.Instance.SendIntent(intent);
		}

		public async UniTask SendUseSkillIntent(string unitId, int skillId, SkillTarget target)
		{
			var intent = new IntentEnvelope<UseSkillPayload>
			{
				intentId = Guid.NewGuid().ToString(),
				userId = AuthManager.Instance.UserId,
				matchId = NetworkManager.Instance.MatchId,
				name = "UseSkill",
				payload = new UseSkillPayload { unitId = unitId, skillId = skillId, target = target },
				clientTick = GetCurrentClientTick(),
				clientTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};

			await NetworkManager.Instance.SendIntent(intent);
		}

		private long GetCurrentClientTick()
		{
			// Approx 30Hz
			return (long)Math.Floor(Time.realtimeSinceStartupAsDouble / (1.0 / 30.0));
		}
	}
}
