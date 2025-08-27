using System;
using UnityEngine;

namespace ManaGambit
{
	public partial class NetworkManager
	{
		private void WireRoomHandlers()
		{
			if (room == null)
			{
				Debug.LogWarning("[NetworkManager] WireRoomHandlers called with null room");
				return;
			}

			room.OnMessage("*", (string type) =>
			{
				Debug.Log($"{LogTag} OnMessage '*' type='{type}'");
			});

			room.OnMessage<StateSnapshot>("StateSnapshot", snap => { Debug.Log($"{LogTag} StateSnapshot received"); if (snap != null && snap.board != null) SetupBoard(snap.board); });
			room.OnMessage<GameEvent>("GameStart", evt => { if (evt != null && evt.data != null && evt.data.board != null) SetupBoard(evt.data.board); });
			room.OnMessage<GameEvent>("GameAboutToStart", evt => { if (evt != null && evt.data != null && evt.data.board != null) SetupBoard(evt.data.board); });
			room.OnMessage<MoveEvent>("Move", evt => { if (evt != null && evt.data != null) { HandleMove(evt.data); if (!string.IsNullOrEmpty(evt.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(evt.intentId); } });
			room.OnMessage<GameEvent>("ManaUpdate", _ => { /* ignore to avoid warnings for now */ });

			room.OnMessage<ServerEventEnvelopeRaw>("ServerEvent", envelope =>
			{
				if (envelope == null)
				{
					Debug.LogWarning($"{LogTag} ServerEvent received null envelope");
					return;
				}
				if (verboseNetworkLogging)
				{
					Debug.Log($"{LogTag} ServerEvent type={envelope.type} intentId={envelope.intentId} tick={envelope.serverTick}");
				}
				switch (envelope.type)
				{
					case "Move":
						if (envelope.data != null)
						{
							try
							{
								var move = envelope.data.ToObject<MoveData>();
								if (move != null) { HandleMove(move); if (!string.IsNullOrEmpty(envelope.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(envelope.intentId); }
							}
							catch (System.Exception ex)
							{
								Debug.LogWarning($"{LogTag} Failed to parse ServerEvent.Move: {ex.Message}");
							}
						}
						break;
					case "UseSkill":
						try
						{
							var use = envelope.data != null ? envelope.data.ToObject<UseSkillData>() : null;
							if (use != null && !string.IsNullOrEmpty(use.unitId))
							{
								var unit = GameManager.Instance.GetUnitById(use.unitId);
								if (unit != null)
								{
									unit.Attack(new Vector2Int(use.target?.cell?.x ?? unit.CurrentPosition.x, use.target?.cell?.y ?? unit.CurrentPosition.y));
								}
							}
						}
						catch (System.Exception) { /* ignore */ }
						break;
					case "UseSkillResult":
						try
						{
							var res = envelope.data != null ? envelope.data.ToObject<UseSkillResultData>() : null;
							if (res != null && res.targets != null)
							{
								for (int i = 0; i < res.targets.Length; i++)
								{
									var t = res.targets[i];
									if (t != null && t.killed)
									{
										var victim = GameManager.Instance.GetUnitById(t.unitId);
										if (victim != null)
										{
											Board.Instance.ClearOccupied(victim.CurrentPosition, victim);
											UnityEngine.Object.Destroy(victim.gameObject);
											GameManager.Instance.UnregisterUnit(t.unitId);
										}
									}
								}
								if (!string.IsNullOrEmpty(envelope.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(envelope.intentId);
							}
						}
						catch (System.Exception) { /* ignore */ }
						break;
					default:
						if (verboseNetworkLogging)
						{
							Debug.Log($"{LogTag} Unhandled ServerEvent type={envelope.type}");
						}
						break;
				}
			});

			room.OnMessage<GameEvent>("StateHeartbeat", _ => { /* ignore */ });
			room.OnMessage<UseSkillEvent>("UseSkill", evt => { /* TODO: trigger cast visuals if needed */ });
			room.OnMessage<UseSkillResultEvent>("UseSkillResult", evt => { /* TODO: show impacts/damage */ });
			room.OnMessage<StatusUpdateEvent>("StatusUpdate", evt =>
			{
				try
				{
					var unitId = evt != null && evt.data != null ? evt.data.unitId : null;
					if (!string.IsNullOrEmpty(unitId))
					{
						var unit = GameManager.Instance.GetUnitById(unitId);
						if (unit != null)
						{
							unit.ApplyStatusChanges(evt.data.changes);
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"{LogTag} StatusUpdate handler exception: {ex.Message}");
				}
			});
			room.OnMessage<GameOverEvent>("GameOver", evt => { Debug.Log($"{LogTag} GameOver: reason={evt?.data?.reason} winner={evt?.data?.winnerUserId}"); });
			room.OnMessage<ErrorEvent>("Error", evt =>
			{
				if (evt == null || evt.data == null)
				{
					Debug.LogWarning($"{LogTag} Error (no data)");
				}
				else
				{
					Debug.LogWarning($"{LogTag} Error: code={evt.data.code} msg={evt.data.msg} iid={evt.data.iid} retry={evt.data.retry}");
					if (IntentManager.Instance != null)
					{
						IntentManager.Instance.HandleIntentError(evt);
					}
				}
			});
			room.OnMessage<ValidTargetsEvent>("ValidTargets", evt =>
			{
				int count = evt?.data?.targets != null ? evt.data.targets.Length : 0;
				Debug.Log($"{LogTag} ValidTargets received for unit={evt?.data?.unitId} count={count}");
				OnValidTargets?.Invoke(evt);
			});
		}
	}
}

