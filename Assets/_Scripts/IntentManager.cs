
using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
	public class IntentManager : MonoBehaviour
	{
		public static IntentManager Instance { get; private set; }

		// Track intents for de-duplication/latency logging similar to JS client
		private readonly Dictionary<string, long> pendingIntentSentAtMs = new Dictionary<string, long>();

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
			TrackPending(intent.intentId);
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
			TrackPending(intent.intentId);
			await NetworkManager.Instance.SendIntent(intent);
		}

		public async UniTask SendCancelCastIntent(string unitId, string castId)
		{
			var intent = new IntentEnvelope<CancelCastPayload>
			{
				intentId = Guid.NewGuid().ToString(),
				userId = AuthManager.Instance.UserId,
				matchId = NetworkManager.Instance.MatchId,
				name = "CancelCast",
				payload = new CancelCastPayload { unitId = unitId, castId = castId },
				clientTick = GetCurrentClientTick(),
				clientTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
			TrackPending(intent.intentId);
			await NetworkManager.Instance.SendIntent(intent);
		}

		public async UniTask SendLeaveMatchIntent(string reason = "UserQuit")
		{
			var intent = new IntentEnvelope<LeaveMatchPayload>
			{
				intentId = Guid.NewGuid().ToString(),
				userId = AuthManager.Instance.UserId,
				matchId = NetworkManager.Instance.MatchId,
				name = "LeaveMatch",
				payload = new LeaveMatchPayload { reason = reason },
				clientTick = GetCurrentClientTick(),
				clientTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
			TrackPending(intent.intentId);
			await NetworkManager.Instance.SendIntent(intent);
		}

		private long GetCurrentClientTick()
		{
			// Approx 30Hz
			return (long)Math.Floor(Time.realtimeSinceStartupAsDouble / (1.0 / 30.0));
		}

		private void TrackPending(string intentId)
		{
			if (string.IsNullOrEmpty(intentId)) return;
			pendingIntentSentAtMs[intentId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}

		public void HandleIntentResponse(string intentId)
		{
			if (string.IsNullOrEmpty(intentId)) return;
			if (pendingIntentSentAtMs.TryGetValue(intentId, out var sentMs))
			{
				long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				long delta = now - sentMs;
				Debug.Log($"[IntentManager] Intent {intentId} response in {delta}ms");
				pendingIntentSentAtMs.Remove(intentId);
			}
		}

		public void HandleIntentError(ErrorEvent evt)
		{
			if (evt == null || evt.data == null) return;
			string iid = evt.data.iid;
			if (string.IsNullOrEmpty(iid)) return;
			if (pendingIntentSentAtMs.ContainsKey(iid))
			{
				pendingIntentSentAtMs.Remove(iid);
			}
			Debug.LogWarning($"[IntentManager] Intent {iid} failed: code={evt.data.code} msg={evt.data.msg}");
		}
	}
}
