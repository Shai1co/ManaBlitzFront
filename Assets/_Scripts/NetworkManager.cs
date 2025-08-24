
using Colyseus;
using Colyseus.Schema;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;

namespace ManaGambit
{
	public class NetworkManager : MonoBehaviour
	{
		private const string LogTag = "[NetworkManager]";
		private const string JoinQueuePath = "queue/join";
		private const string ConfigPath = "config";
		public static NetworkManager Instance { get; private set; }

		private static readonly Vector3 BoardSpawnOffset = new Vector3(4f, 0f, 4f);

		[SerializeField] private string serverUrl = "https://manablitz.onrender.com/";

		private ColyseusClient colyseusClient;
		private ColyseusRoom<RoomState> room;

		[SerializeField] private UnitConfig unitConfig;  // Assign in Inspector

		// Spawn orientation configuration (editable in Inspector)
		[SerializeField, Tooltip("X-axis rotation offset applied to spawned units (degrees)")]
		private float spawnRotationOffsetXDeg = 15f;
		[SerializeField, Tooltip("Yaw for units owned by local player (degrees)")]
		private float ownUnitYawDeg = 0f;
		[SerializeField, Tooltip("Yaw for enemy units (degrees)")]
		private float enemyUnitYawDeg = 180f;

		// Values captured from server
		public string MatchId { get; private set; }
		public string RulesHash { get; private set; }

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		public async UniTask<bool> FetchConfig()
		{
			if (string.IsNullOrEmpty(AuthManager.Instance.Token))
			{
				Debug.LogError($"{LogTag} Must be authenticated to fetch config");
				return false;
			}

			string url = serverUrl + ConfigPath;
			var request = new UnityWebRequest(url, "GET");
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Authorization", $"Bearer {AuthManager.Instance.Token}");

			var operation = request.SendWebRequest();
			await UniTask.WaitUntil(() => operation.isDone);

			Debug.Log($"{LogTag} GET {url} responseCode={(long)request.responseCode} result={request.result} error={request.error} body(len)={request.downloadHandler.text?.Length ?? 0}");
			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"{LogTag} Fetch config failed: {request.error} (HTTP {(long)request.responseCode})");
				return false;
			}

			try
			{
				var cfg = JsonUtility.FromJson<CombinedConfig>(request.downloadHandler.text);
				RulesHash = cfg.rulesHash;
				Debug.Log($"{LogTag} Config fetched. characters={(cfg.characters != null ? cfg.characters.Length : 0)}, rulesHash={RulesHash}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"{LogTag} Failed to parse /config response: {ex}\nBody: {request.downloadHandler.text}");
				return false;
			}

			return true;
		}

		public async UniTask JoinQueue(string mode = "practice")
		{
			if (string.IsNullOrEmpty(AuthManager.Instance.Token))
			{
				Debug.LogError("Must be authenticated to join queue");
				return;
			}

			string url = serverUrl + JoinQueuePath;
			var payload = new JoinQueueRequest { region = "auto", mode = mode };
			string json = JsonUtility.ToJson(payload);
			Debug.Log($"{LogTag} POST {url} body={json}");
			var request = new UnityWebRequest(url, "POST");
			byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
			request.uploadHandler = new UploadHandlerRaw(body);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Authorization", $"Bearer {AuthManager.Instance.Token}");

			var operation = request.SendWebRequest();
			await UniTask.WaitUntil(() => operation.isDone);

			Debug.Log($"{LogTag} JoinQueue responseCode={(long)request.responseCode} result={request.result} error={request.error} body={request.downloadHandler.text}");
			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"{LogTag} Join queue failed: {request.error} (HTTP {(long)request.responseCode})");
				return;
			}

			JoinQueueResponse response;
			try
			{
				response = JsonUtility.FromJson<JoinQueueResponse>(request.downloadHandler.text);
			}
			catch (Exception ex)
			{
				Debug.LogError($"{LogTag} Failed to parse JoinQueue response: {ex}\nBody: {request.downloadHandler.text}");
				return;
			}

			// Resolve WebSocket endpoint and room information with fallbacks
			string endpoint =
				response?.reservation?.address ??
				response?.reservation?.endpoint ??
				response?.reservation?.wsEndpoint ??
				response?.endpoint ??
				response?.wsEndpoint;

			if (string.IsNullOrEmpty(endpoint))
			{
				endpoint = BuildWebSocketEndpointFromHttp(serverUrl);
				Debug.LogWarning($"{LogTag} No endpoint provided by response; derived endpoint='{endpoint}' from serverUrl='{serverUrl}'");
			}

			string roomId = response?.roomId ?? response?.reservation?.roomId ?? response?.reservation?.room?.id ?? response?.reservation?.room?.roomId;
			string joinToken = response?.reservation?.joinToken;
			MatchId = response?.matchId;
			var sessionId = response?.reservation?.sessionId;

			Debug.Log($"{LogTag} Connecting to endpoint='{endpoint}', roomId='{roomId}', reservationTokenPresent={(string.IsNullOrEmpty(joinToken) ? "false" : "true")}, sessionIdPresent={(string.IsNullOrEmpty(sessionId) ? "false" : "true")}, matchId='{MatchId}'");

			if (string.IsNullOrEmpty(endpoint))
			{
				Debug.LogError($"{LogTag} Endpoint is empty. Cannot create ColyseusClient. Body: {request.downloadHandler.text}");
				return;
			}
			if (string.IsNullOrEmpty(roomId))
			{
				Debug.LogError($"{LogTag} roomId is missing from response. Body: {request.downloadHandler.text}");
				return;
			}
			if (string.IsNullOrEmpty(joinToken) && string.IsNullOrEmpty(sessionId))
			{
				Debug.LogError($"{LogTag} Neither joinToken nor sessionId present. Body: {request.downloadHandler.text}");
				return;
			}

			Dictionary<string, object> joinOptions;
			if (!string.IsNullOrEmpty(joinToken))
			{
				// Preferred per server doc
				joinOptions = new Dictionary<string, object> { { "token", joinToken } };
			}
			else
			{
				// Fallback to Colyseus sessionId flow if token is not provided
				joinOptions = new Dictionary<string, object> { { "sessionId", sessionId } };
			}
			// Provide identifiers expected by onAuth checks
			joinOptions["userId"] = AuthManager.Instance.UserId;
			if (!string.IsNullOrEmpty(MatchId)) joinOptions["matchId"] = MatchId;

			// Prefer consuming the seat reservation directly using sessionId
			var canConsumeDirectly = response?.reservation?.room != null && !string.IsNullOrEmpty(sessionId);

			colyseusClient = new ColyseusClient(endpoint);
			// Ensure JWT is also available via _authToken query param during WS connect
			colyseusClient.Http.AuthToken = AuthManager.Instance.Token;

			if (canConsumeDirectly)
			{
				var mm = new ColyseusMatchMakeResponse
				{
					room = new ColyseusRoomAvailable
					{
						name = response.reservation.room.name,
						processId = response.reservation.room.processId,
						roomId = response.reservation.room.roomId
					},
					sessionId = sessionId
				};

				var headers = new Dictionary<string, string>
				{
					{ "Authorization", $"Bearer {AuthManager.Instance.Token}" }
				};

				room = await colyseusClient.ConsumeSeatReservation<RoomState>(mm, headers);
			}
			else
			{
				// Join by id with whatever the server expects (token/sessionId) and auth headers
				var headers = new Dictionary<string, string>
				{
					{ "Authorization", $"Bearer {AuthManager.Instance.Token}" }
				};
				room = await colyseusClient.JoinById<RoomState>(roomId, joinOptions, headers);
			}

			Debug.Log($"{LogTag} Joined room. roomId='{roomId}', sessionId='{room.SessionId}'");
			// Log all message types for visibility during bring-up
			room.OnMessage("*", (string type) =>
			{
				Debug.Log($"{LogTag} OnMessage '*' type='{type}'");
			});
			// Typed handlers per Colyseus C# API
			room.OnMessage<StateSnapshot>("StateSnapshot", snap => { Debug.Log($"{LogTag} StateSnapshot received"); if (snap != null && snap.board != null) SetupBoard(snap.board); });
			room.OnMessage<GameEvent>("GameStart", evt => { if (evt != null && evt.data != null && evt.data.board != null) SetupBoard(evt.data.board); });
			room.OnMessage<GameEvent>("GameAboutToStart", evt => { if (evt != null && evt.data != null && evt.data.board != null) SetupBoard(evt.data.board); });
			room.OnMessage<MoveEvent>("Move", evt => { if (evt != null && evt.data != null) HandleMove(evt.data); });
			room.OnMessage<GameEvent>("ManaUpdate", _ => { /* ignore to avoid warnings for now */ });
			room.OnMessage<GameEvent>("StateHeartbeat", _ => { /* ignore */ });
			room.OnMessage<UseSkillEvent>("UseSkill", evt => { /* TODO: trigger cast visuals if needed */ });
			room.OnMessage<UseSkillResultEvent>("UseSkillResult", evt => { /* TODO: show impacts/damage */ });
			room.OnMessage<StatusUpdateEvent>("StatusUpdate", evt => { /* TODO: update status icons */ });
			room.OnMessage<GameOverEvent>("GameOver", evt => { Debug.Log($"{LogTag} GameOver: reason={evt?.data?.reason} winner={evt?.data?.winnerUserId}"); });
			room.OnMessage<ErrorEvent>("Error", evt => { Debug.LogWarning($"{LogTag} Error: code={evt?.data?.code} msg={evt?.data?.msg}"); });
			room.OnMessage<ValidTargetsEvent>("ValidTargets", evt => { OnValidTargets?.Invoke(evt); });
		}

		public System.Action<ValidTargetsEvent> OnValidTargets;

		public void RequestValidTargets(string unitId, string name = "Move")
		{
			if (room == null) return;
			var req = new RequestValidTargets { unitId = unitId, name = name };
			room.Send("RequestValidTargets", req);
		}

		public async UniTask SendIntent<TPayload>(IntentEnvelope<TPayload> envelope)
		{
			if (room == null)
			{
				Debug.LogWarning($"{LogTag} Cannot send intent; room is null");
				return;
			}
			try
			{
				room.Send("Intent", envelope);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"{LogTag} SendIntent failed: {ex.Message}");
			}
		}

		private void OnDisable()
		{
			_ = CleanupAsync();
		}

		private void OnDestroy()
		{
			_ = CleanupAsync();
		}

		private void OnApplicationQuit()
		{
			_ = CleanupAsync();
		}

		private async UniTaskVoid CleanupAsync()
		{
			try
			{
				if (room != null)
				{
					await room.Leave();
					room = null;
				}
				if (colyseusClient != null)
				{
					colyseusClient = null;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"{LogTag} Cleanup exception: {ex.Message}");
			}
		}

		private void SetupBoard(BoardData board)
		{
			if (board == null)
			{
				Debug.LogWarning($"{LogTag} SetupBoard called with null board");
				return;
			}
			Debug.Log($"{LogTag} SetupBoard width={board.width} height={board.height} units={(board.units != null ? board.units.Length : 0)}");
			foreach (var unit in GameManager.Instance.GetAllUnits())
			{
				Destroy(unit.gameObject);
			}
			GameManager.Instance.ClearUnits();

			if (board.units == null || board.units.Length == 0)
			{
				Debug.LogWarning($"{LogTag} Board has no units in snapshot");
				return;
			}

			for (int i = 0; i < board.units.Length; i++)
			{
				var unitData = board.units[i];
				var prefab = unitConfig != null ? unitConfig.GetPrefab(unitData.pieceId) : null;
				if (prefab == null)
				{
					Debug.LogWarning($"{LogTag} No prefab mapped for pieceId='{unitData.pieceId}' (unitId='{unitData.unitId}'). Assign in UnitConfig.");
					continue;
				}

				var cell = new Vector2Int(unitData.pos.x, unitData.pos.y);
				Transform slot;
				GameObject instance;
				if (Board.Instance.TryGetSlot(cell, out slot) && slot != null)
				{
					var spawnPos = slot.position;
					instance = Instantiate(prefab, spawnPos, Quaternion.identity);
				}
				else
				{
					var spawnPos = Board.Instance.GetWorldPosition(cell);
					instance = Instantiate(prefab, spawnPos, Quaternion.identity);
					Debug.LogWarning($"{LogTag} Slot not found for {cell}. Using grid fallback.");
				}
				var unit = instance.GetComponent<Unit>();
				unit.SetUnitId(unitData.unitId);
				unit.SetPieceId(unitData.pieceId);
				unit.SetOwnerId(unitData.ownerId);
				unit.SetInitialData(unitData, false);

				// Apply rotation offsets
				var isOwnUnit = string.Equals(unitData.ownerId, AuthManager.Instance.UserId);
				var local = instance.transform.localEulerAngles;
				local.x = spawnRotationOffsetXDeg;
				local.y = isOwnUnit ? ownUnitYawDeg : enemyUnitYawDeg;
				instance.transform.localEulerAngles = local;

				GameManager.Instance.RegisterUnit(unit, unitData.unitId);
				// Mark occupied
				Board.Instance.SetOccupied(cell, unit);
			}
		}

		private void HandleMove(MoveData moveData)
		{
			var unit = GameManager.Instance.GetUnitById(moveData.unitId);
			if (unit != null)
			{
				// Clear old occupancy
				Board.Instance.ClearOccupied(unit.CurrentPosition, unit);
				unit.MoveTo(new Vector2Int(moveData.to.x, moveData.to.y)).Forget();
				// Mark new occupancy immediately (temporary until server patch confirms)
				Board.Instance.SetOccupied(new Vector2Int(moveData.to.x, moveData.to.y), unit);
			}
			else
			{
				Debug.LogWarning($"Unit not found for move: {moveData.unitId}");
			}
		}

		public void ShowHighlightsForSelection()
		{
			Board.Instance.ShowAvailableHighlights();
		}

		public void HideAllHighlights()
		{
			Board.Instance.HideAllHighlights();
		}

		// Colyseus state sync can be wired here later if needed

		[Serializable]
		private class JoinQueueResponse
		{
			public string roomId;
			public Reservation reservation;
			public string matchId;
			public string endpoint;
			public string wsEndpoint;
		}

		[Serializable]
		private class Reservation
		{
			public string sessionId;
			public string address;
			public string joinToken;
			public string endpoint;
			public string wsEndpoint;
			public string roomId;
			public Room room;
		}

		[Serializable]
		private class Room
		{
			public string id;
			public string roomId;
			public string name;
			public string processId;
		}

		[Serializable]
		private class JoinQueueRequest
		{
			public string region;
			public string mode;
		}

		[Serializable]
		private class CombinedConfig
		{
			public CharacterDef[] characters;
			public Constants constants;
			public string etag;
			public string lastModified;
			public string rulesHash;
		}

		[Serializable]
		private class CharacterDef { }

		[Serializable]
		private class Constants
		{
			public int pregameCountdownTicks;
			public int stateHeartbeatIntervalTicks;
			public int manaUpdateIntervalTicks;
			public int rejoinGraceSeconds;
			public int protocolVersion;
		}

		[Serializable]
		private class StateSnapshot
		{
			public BoardData board;
		}

		private static string BuildWebSocketEndpointFromHttp(string httpUrl)
		{
			try
			{
				var uri = new Uri(httpUrl);
				string scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
				string host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
				return $"{scheme}://{host}";
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}
	}
}
