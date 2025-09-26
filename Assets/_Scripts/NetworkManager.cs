
using Colyseus;
using Colyseus.Schema;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;
using System.Threading;

namespace ManaGambit
{
	public class NetworkManager : MonoBehaviour
	{
		private const string LogTag = "[NetworkManager]";
		private const string JoinQueuePath = "queue/join";
		private const string ConfigPath = "config";
		private const string IntentChannel = "Intent"; // Match JS client/server message type
		private const float ReconnectInitialDelaySeconds = (15f * DefaultTickRateMs) / 1000f; // 15 ticks
		private const float ReconnectMaxDelaySeconds = (240f * DefaultTickRateMs) / 1000f; // 240 ticks
		private const int ReconnectMaxAttempts = 3;
	private const int ReconnectingFalse = 0; // for atomic gate
	private const int ReconnectingTrue = 1;  // for atomic gate
	private const long HttpNotModifiedCode = 304;
	public static NetworkManager Instance { get; private set; }

		public const float DefaultTickRateMs = 33.333f; // Centralized server tick rate

		private static readonly Vector3 BoardSpawnOffset = new Vector3(4f, 0f, 4f);

		// Server URL is now centralized in ServerConfig
		private string cachedEtag = null;
		private string lastConfigJson = null;

		private ColyseusClient colyseusClient;
		private ColyseusRoom<RoomState> room;
		private string lastRoomId;
		private string lastSessionId;
		private ReconnectionToken lastReconnectionToken;
		private bool isRoomOpen;
		private bool isReconnecting;
		private int reconnectingGate = ReconnectingFalse;
		private int reconnectAttempts;
		private CancellationTokenSource _reconnectCts;

		[SerializeField] private UnitConfig unitConfig;  // Assign in Inspector
		[SerializeField] private bool verboseNetworkLogging = false;
		[SerializeField, Tooltip("If true, client will request ValidTargets from server when available (JS client does not). Default: false")] private bool serverSupportsValidTargets = false;
		public UnitConfig UnitConfigAsset => unitConfig;
		public bool ServerSupportsValidTargets => serverSupportsValidTargets;

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

		/// <summary>
		/// Deserializes event data from JSON with error handling
		/// </summary>
		/// <typeparam name="T">The type to deserialize to</typeparam>
		/// <param name="data">The data to deserialize</param>
		/// <returns>The deserialized object or null if deserialization fails</returns>
		private T DeserializeEventData<T>(object data) where T : class
		{
			if (data == null)
			{
				return null;
			}

			try
			{
				return JObject.FromObject(data).ToObject<T>();
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"{LogTag} Failed to deserialize {typeof(T).Name}: {ex.Message}");
				return null;
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
			
			// Initialize cancellation token source for reconnection attempts
			_reconnectCts = new CancellationTokenSource();
		}

		/// <summary>
		/// Logs a server message in the exact JS client format: [MessageType] [server] -> {JSON}
		/// </summary>
		/// <param name="messageType">The server message type</param>
		/// <param name="data">The message data</param>
		/// <param name="logTag">Optional log tag prefix</param>
		private static void LogServerMessage(string messageType, object data, string logTag = "[NetworkManager]")
		{
			if (string.IsNullOrEmpty(messageType) || data == null) return;
			try
			{
				string jsonData = JsonUtility.ToJson(data);
				Debug.Log($"{logTag} [{messageType}] [server] -> {jsonData}");
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"{logTag} Failed to serialize server message {messageType}: {ex.Message}");
			}
		}

		/// <summary>
		/// Helper method to extract maxMana value from config constants using reflection.
		/// Tries both property and field named "maxMana", validates it's a positive int,
		/// and returns the default value when missing or invalid.
		/// </summary>
		/// <param name="constants">The constants object to search for maxMana</param>
		/// <param name="defaultValue">Default value to return if maxMana is not found or invalid</param>
		/// <returns>The resolved maxMana value or the default if not found/invalid</returns>
		private static int GetMaxManaFromConstants(object constants, int defaultValue = 10)
		{
			if (constants == null) return defaultValue;

			var constantsType = constants.GetType();
			
			// Try property first
			var maxManaProp = constantsType.GetProperty("maxMana");
			if (maxManaProp != null)
			{
				try
				{
					object value = maxManaProp.GetValue(constants, null);
					if (value is int intValue && intValue > 0)
					{
						return intValue;
					}
				}
				catch { /* ignore reflection errors */ }
			}
			
			// Try field if property not found or failed
			var maxManaField = constantsType.GetField("maxMana");
			if (maxManaField != null)
			{
				try
				{
					object value = maxManaField.GetValue(constants);
					if (value is int intValue && intValue > 0)
					{
						return intValue;
					}
				}
				catch { /* ignore reflection errors */ }
			}
			
			return defaultValue;
		}

		public async UniTask<bool> FetchConfig()
		{
			if (string.IsNullOrEmpty(AuthManager.Instance.Token))
			{
				Debug.LogError($"{LogTag} Must be authenticated to fetch config");
				return false;
			}

			string url = ServerConfig.ServerUrl + ConfigPath;
			var request = new UnityWebRequest(url, "GET");
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Authorization", $"Bearer {AuthManager.Instance.Token}");
			if (!string.IsNullOrEmpty(cachedEtag))
			{
				request.SetRequestHeader("If-None-Match", cachedEtag);
			}

			var operation = request.SendWebRequest();
			await UniTask.WaitUntil(() => operation.isDone);

			Debug.Log($"{LogTag} GET {url} responseCode={(long)request.responseCode} result={request.result} error={request.error} body(len)={request.downloadHandler.text?.Length ?? 0}");
			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"{LogTag} Fetch config failed: {request.error} (HTTP {(long)request.responseCode})");
				return false;
			}
		// Handle 304 Not Modified
		if ((long)request.responseCode == HttpNotModifiedCode)
			{
				Debug.Log($"{LogTag} Config not modified (ETag match). Using cached config.");
				return !string.IsNullOrEmpty(lastConfigJson);
			}

			try
			{
				var body = request.downloadHandler.text;
				var cfg = JsonUtility.FromJson<CombinedConfig>(body);
				RulesHash = cfg.rulesHash;
				Debug.Log($"{LogTag} Config fetched. characters={(cfg.characters != null ? cfg.characters.Length : 0)}, rulesHash={RulesHash}");
				lastConfigJson = body;
				// Apply max mana to UI if available in constants
				try
				{
					var manaBar = ManaBarUI.Instance != null
					               ? ManaBarUI.Instance
					               : FindFirstObjectByType<ManaBarUI>();
					if (manaBar != null && cfg != null && cfg.constants != null)
					{
						int maxMana = GetMaxManaFromConstants(cfg.constants);
						manaBar.SetMaxPips(maxMana);
					}
				}
				catch { /* ignore */ }
				if (request.GetResponseHeaders() != null && request.GetResponseHeaders().TryGetValue("ETag", out var etag))
				{
					cachedEtag = etag;
				}
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
			// Backward compatibility: convert mode string to GameLaunchConfig
			GameLaunchConfig config;
			switch (mode.ToLower())
			{
				case "arena":
				case "online":
					config = GameLaunchConfig.PlayOnline();
					break;
				case "easy":
					config = GameLaunchConfig.VsBot(BotDifficulty.Easy);
					break;
				case "medium":
					config = GameLaunchConfig.VsBot(BotDifficulty.Medium);
					break;
				case "hard":
					config = GameLaunchConfig.VsBot(BotDifficulty.Hard);
					break;
				default:
					config = GameLaunchConfig.PlayOnline();
					break;
			}
			
			await JoinQueue(config);
		}

		public async UniTask JoinQueue(GameLaunchConfig gameConfig)
		{
			if (string.IsNullOrEmpty(AuthManager.Instance.Token))
			{
				Debug.LogError("Must be authenticated to join queue");
				return;
			}

			string url = ServerConfig.ServerUrl + JoinQueuePath;
			var payload = new JoinQueueRequest 
			{ 
				region = "auto", 
				mode = gameConfig.ToServerMode(),
				difficulty = gameConfig.GetDifficultyString()
			};
			string json = JsonUtility.ToJson(payload);
			Debug.Log($"{LogTag} POST {url} body={json} gameConfig={gameConfig}");
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
				endpoint = BuildWebSocketEndpointFromHttp(ServerConfig.ServerUrl);
				Debug.LogWarning($"{LogTag} No endpoint provided by response; derived endpoint='{endpoint}' from serverUrl='{ServerConfig.ServerUrl}'");
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
			// cache for reconnects
			lastRoomId = room.RoomId;
			lastSessionId = room.SessionId;
			lastReconnectionToken = room.ReconnectionToken;
			WireRoomHandlers();
		}

		public System.Action<ValidTargetsEvent> OnValidTargets;

		public void RequestValidTargets(string unitId, string name = "Move", int skillId = -1)
		{
			if (room == null) return;
			var req = new RequestValidTargets { unitId = unitId, name = name, skillId = skillId };
			if (verboseNetworkLogging)
			{
				Debug.Log($"{LogTag} RequestValidTargets unitId={unitId} name={name} skillId={skillId}");
			}
#pragma warning disable CS4014 // Fire-and-forget send operation
			room.Send("RequestValidTargets", req);
#pragma warning restore CS4014
		}

		public UniTask SendIntent<TPayload>(IntentEnvelope<TPayload> envelope)
		{
			if (room == null || !isRoomOpen)
			{
				Debug.LogWarning($"{LogTag} Cannot send intent; room is {(room==null ? "null" : "closed")}. Attempting reconnect...");
				_ = TryAutoReconnect();
				return UniTask.CompletedTask;
			}
			try
			{
				if (verboseNetworkLogging)
				{
					string payloadJson = string.Empty;
					try { payloadJson = JsonUtility.ToJson(envelope.payload); } catch { }
					Debug.Log($"{LogTag} SendIntent name={envelope.name} iid={envelope.intentId} matchId={envelope.matchId} userId={envelope.userId} payload={payloadJson}");
				}
#pragma warning disable CS4014 // Fire-and-forget send operation
				room.Send(IntentChannel, envelope);
#pragma warning restore CS4014
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"{LogTag} SendIntent failed: {ex.Message}");
			}
		return UniTask.CompletedTask;
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

		private async UniTask CleanupAsync()
		{
			try
			{
				// Cancel any ongoing reconnection attempts
				_reconnectCts?.Cancel();
				_reconnectCts?.Dispose();
				_reconnectCts = null;
				
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

		public async UniTask Disconnect()
		{
			await CleanupAsync();
		}

		public async UniTask Reconnect()
		{
			try
			{
				if (colyseusClient == null || lastReconnectionToken == null)
				{
					Debug.LogWarning($"{LogTag} Cannot reconnect: missing client or reconnection token");
					return;
				}
				room = await colyseusClient.Reconnect<RoomState>(lastReconnectionToken);
				Debug.Log($"{LogTag} Reconnected to room. sessionId='{room.SessionId}'");
				lastRoomId = room.RoomId;
				lastSessionId = room.SessionId;
				lastReconnectionToken = room.ReconnectionToken;
				WireRoomHandlers();
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"{LogTag} Reconnect failed: {ex.Message}");
			}
		}

		private void SetupBoard(BoardData board)
		{
			SetupBoard(board, true);
		}

		private void SetupBoard(BoardData board, bool forceFullReset)
		{
			if (board == null)
			{
				Debug.LogWarning($"{LogTag} SetupBoard called with null board");
				return;
			}
			Debug.Log($"{LogTag} SetupBoard width={board.width} height={board.height} units={(board.units != null ? board.units.Length : 0)} forceReset={forceFullReset}");
			
			// Only do full reset for initial setup or when explicitly requested
			if (forceFullReset)
			{
				// Before destroying units, trigger death events for any that might be selected
				// This ensures ClickInput clears its selection state properly
				var allUnits = GameManager.Instance.GetAllUnits();
				foreach (var unit in allUnits)
				{
					if (unit != null && !unit.IsDead)
					{
						// Trigger death event to clear any selection/highlighting state
						Unit.TriggerUnitDeathEvent(unit);
					}
					
					// For board setup, we can destroy immediately since this is cleanup/reset
					Destroy(unit.gameObject);
				}
				GameManager.Instance.ClearUnits();
			}

			if (board.units == null || board.units.Length == 0)
			{
				if (forceFullReset)
				{
					Debug.LogWarning($"{LogTag} Board has no units in snapshot");
				}
				return;
			}

			// If not forcing reset, try to update existing units smoothly
			if (!forceFullReset)
			{
				UpdateBoardIncremental(board);
				return;
			}

			// Full reset: create all units from scratch
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
				local.x = isOwnUnit ? spawnRotationOffsetXDeg : -spawnRotationOffsetXDeg;
				local.y = isOwnUnit ? ownUnitYawDeg : enemyUnitYawDeg;
				instance.transform.localEulerAngles = local;

				GameManager.Instance.RegisterUnit(unit, unitData.unitId);
				// Mark occupied
				Board.Instance.SetOccupied(cell, unit);
			}
		}

		private void UpdateBoardIncremental(BoardData board)
		{
			if (board == null || board.units == null) return;
			
			Debug.Log($"{LogTag} UpdateBoardIncremental - updating {board.units.Length} units with server data including animation states");
			
			// Update existing units and create new ones as needed
			for (int i = 0; i < board.units.Length; i++)
			{
				var unitData = board.units[i];
				var existingUnit = GameManager.Instance.GetUnitById(unitData.unitId);
				
				if (existingUnit != null)
				{
					// Clear old occupancy before updating position
					var currentPos = existingUnit.CurrentPosition;
					Board.Instance.ClearOccupied(currentPos, existingUnit);
					
					// Apply all server data including position and animation state
					existingUnit.ApplyServerDataUpdate(unitData);
					
					// Mark new occupancy after update
					var newPos = new Vector2Int(unitData.pos.x, unitData.pos.y);
					Board.Instance.SetOccupied(newPos, existingUnit);
				}
				else
				{
					// Unit doesn't exist - create it (this handles new units appearing)
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
					local.x = isOwnUnit ? spawnRotationOffsetXDeg : -spawnRotationOffsetXDeg;
					local.y = isOwnUnit ? ownUnitYawDeg : enemyUnitYawDeg;
					instance.transform.localEulerAngles = local;

					GameManager.Instance.RegisterUnit(unit, unitData.unitId);
					// Mark occupied
					Board.Instance.SetOccupied(cell, unit);
				}
			}
			
			// Handle units that no longer exist in the server state (remove them)
			var allUnits = GameManager.Instance.GetAllUnits();
			var serverUnitIds = new System.Collections.Generic.HashSet<string>();
			for (int i = 0; i < board.units.Length; i++)
			{
				serverUnitIds.Add(board.units[i].unitId);
			}
			
			for (int i = allUnits.Count - 1; i >= 0; i--)
			{
				var unit = allUnits[i];
				if (!serverUnitIds.Contains(unit.UnitID))
				{
					// Unit no longer exists on server - remove it
					Board.Instance.ClearOccupied(unit.CurrentPosition, unit);
					// CRITICAL FIX: Clean up skill tracking to prevent memory leaks
					CleanupUnitSkillTracking(unit.UnitID);
					GameManager.Instance.UnregisterUnit(unit.UnitID);
					unit.Die(); // Use death animation instead of immediate destruction
				}
			}
			
			// After all board updates, refresh highlights for any selected unit to ensure they're current
			// This handles cases where positions changed or board occupancy changed
			try
			{
				var clickInput = FindFirstObjectByType<ClickInput>();
				if (clickInput != null)
				{
					// Call the public method directly instead of using reflection
					clickInput.RefreshHighlightsForSelectedUnit();
				}
			}
			catch (System.Exception e)
			{
				Debug.LogWarning($"{LogTag} Error refreshing highlights after board update: {e.Message}");
			}
		}

		/// <summary>
		/// Extracts and sets player names for the HUD from available data sources.
		/// Uses server playerNames if available, otherwise derives from AuthManager and board units.
		/// </summary>
		/// <param name="serverPlayerNames">Player names from server (optional)</param>
		/// <param name="board">Board data to extract opponent from (optional)</param>
		private void UpdatePlayerNamesUI(Dictionary<string, string> serverPlayerNames = null, BoardData board = null)
		{
			if (HudController.Instance == null) return;

			try
			{
				string localPlayerName = "Player";
				string opponentPlayerName = "Opponent";

				// Get local player name from AuthManager
				if (!string.IsNullOrEmpty(AuthManager.Instance?.UserId))
				{
					// Try to get from server data first
					if (serverPlayerNames != null && serverPlayerNames.TryGetValue(AuthManager.Instance.UserId, out var serverLocalName))
					{
						localPlayerName = serverLocalName;
					}
					else
					{
						// Fallback to AuthManager data (note: AuthManager doesn't currently store username, only UserId)
						// For now, use a placeholder until server provides names
						localPlayerName = "You";
					}
				}

				// Get opponent name
				if (board?.units != null)
				{
					string localUserId = AuthManager.Instance?.UserId;
					if (!string.IsNullOrEmpty(localUserId))
					{
						// Find the first unit with a different ownerId
						foreach (var unit in board.units)
						{
							if (!string.IsNullOrEmpty(unit.ownerId) && !string.Equals(unit.ownerId, localUserId))
							{
								// Try to get opponent name from server data
								if (serverPlayerNames != null && serverPlayerNames.TryGetValue(unit.ownerId, out var serverOpponentName))
								{
									opponentPlayerName = serverOpponentName;
								}
								else
								{
									// Use a more descriptive placeholder
									opponentPlayerName = "Opponent";
								}
								break;
							}
						}
					}
				}

				// SetPlayerNames will show the UI and update names - safe to call multiple times
				HudController.Instance.SetPlayerNames(localPlayerName, opponentPlayerName);
				Debug.Log($"{LogTag} Updated player names: Local='{localPlayerName}', Opponent='{opponentPlayerName}'");
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"{LogTag} Failed to update player names UI: {ex.Message}");
			}
		}

		private void HandleMove(MoveData moveData)
		{
			Debug.Log($"{LogTag} HandleMove unitId={moveData.unitId} to={moveData.to.x},{moveData.to.y} animState={moveData.animState} reason={moveData.reason} moveStyle={moveData.moveStyle}");
			var unit = GameManager.Instance.GetUnitById(moveData.unitId);
			if (unit != null)
			{
				// Determine movement characteristics first (needed for animation and duration logic)
				bool isPostImpactMove = !string.IsNullOrEmpty(moveData.reason) && moveData.reason.Equals("PostImpact", System.StringComparison.OrdinalIgnoreCase);
				bool isSwapMove = !string.IsNullOrEmpty(moveData.reason) && moveData.reason.Equals("Swap", System.StringComparison.OrdinalIgnoreCase);
				bool isTeleportMove = !string.IsNullOrEmpty(moveData.moveStyle) && moveData.moveStyle.Equals("Teleport", System.StringComparison.OrdinalIgnoreCase);
				bool isApproachMove = !string.IsNullOrEmpty(moveData.reason) && moveData.reason.Equals("Approach", System.StringComparison.OrdinalIgnoreCase);
				
				// CRITICAL FIX: Clear occupancy just before movement starts, not at the beginning
				// This prevents race conditions with rapid move sequences
				
				// Apply animation state - server animState takes precedence but consider movement type
				if (!string.IsNullOrEmpty(moveData.animState))
				{
					var serverAnimState = ParseAnimationState(moveData.animState);
					unit.SetState(serverAnimState);
					Debug.Log($"{LogTag} HandleMove: Applied server animState '{moveData.animState}' -> {serverAnimState} to unit {moveData.unitId}");
				}
				else
				{
					// Determine animation state based on movement type when server doesn't specify
					if (isPostImpactMove)
					{
						// For PostImpact moves, trigger appropriate post-impact animation
						var unitAnimator = unit.GetComponent<UnitAnimator>();
						if (unitAnimator != null)
						{
							// CRITICAL FIX: Get skill index from stored skill data, not just current animator state
							int skillIndex = GetSkillIndexForPostImpact(moveData, unitAnimator);
							bool playedPostImpactAnim = unitAnimator.PlayReturnHomeState(skillIndex);
							Debug.Log($"{LogTag} HandleMove PostImpact: Triggered post-impact animation for unit {moveData.unitId}, skillIndex={skillIndex}, success={playedPostImpactAnim}");
						}
						// Don't set Moving state for post-impact - let the animation system handle it
					}
					else if (isSwapMove)
					{
						// For Swap moves, use brief moving state
						unit.SetState(UnitState.Moving);
						Debug.Log($"{LogTag} HandleMove Swap: Unit {moveData.unitId} swapping positions");
					}
					else if (isTeleportMove && !isPostImpactMove)
					{
						// Pure teleport (not post-impact) - keep current state
						Debug.Log($"{LogTag} HandleMove Teleport: Unit {moveData.unitId} teleporting instantly");
					}
					else
					{
						// Regular/approach moves
						unit.SetState(UnitState.Moving);
					}
				}
				
				// Compute duration - CRITICAL FIX: Handle teleport moves properly regardless of other flags
				float durationSeconds = 0f;
				try
				{
					if (moveData.startTick > 0 && moveData.endTick > moveData.startTick)
					{
						int dt = moveData.endTick - moveData.startTick;
						durationSeconds = Mathf.Max(DefaultTickRateMs / 1000f, (dt * DefaultTickRateMs) / 1000f);
					}
				}
				catch { durationSeconds = 0f; }
				
				// CRITICAL FIX: Teleport overrides everything - check this first
				if (isTeleportMove)
				{
					durationSeconds = 0f; // Force instant movement for teleports
					Debug.Log($"{LogTag} HandleMove: Teleport movement forced to 0 duration for unit {moveData.unitId}");
				}
				else if (isSwapMove)
				{
					// Swap should be quick but visible
					durationSeconds = Mathf.Max(durationSeconds, 0.2f);
				}
				
				var dest = new Vector2Int(moveData.to.x, moveData.to.y);
				
				// Pass movement type info to MoveTo for proper handling
				var moveType = isTeleportMove ? "Teleport" : isPostImpactMove ? "PostImpact" : isSwapMove ? "Swap" : "Normal";
				
				// CRITICAL FIX: Let MoveTo handle all occupancy management to prevent race conditions
				// MoveTo will clear source and set destination occupancy at the correct times
				if (durationSeconds > 0.0001f)
				{
					HandleMoveAsyncWithErrorHandling(unit, dest, durationSeconds, 0f, moveType, moveData.unitId).Forget();
				}
				else
				{
					HandleMoveAsyncWithErrorHandling(unit, dest, 0f, 0f, moveType, moveData.unitId).Forget();
				}
			}
			else
			{
				Debug.LogWarning($"Unit not found for move: {moveData.unitId}");
			}
		}

		// CRITICAL FIX: Track skill indices for PostImpact moves with proper cleanup to prevent memory leaks
		private readonly Dictionary<string, int> unitSkillIndexForPostImpact = new Dictionary<string, int>();
		private readonly Dictionary<string, float> skillIndexTimestamp = new Dictionary<string, float>(); // For cleanup of stale entries
		
		private int GetSkillIndexForPostImpact(MoveData moveData, UnitAnimator unitAnimator)
		{
			// Clean up stale entries (older than 10 seconds) to prevent memory leaks
			CleanupStaleSkillIndices();
			
			// First try to get the stored skill index for PostImpact moves
			if (!string.IsNullOrEmpty(moveData.unitId) && unitSkillIndexForPostImpact.TryGetValue(moveData.unitId, out int storedIndex))
			{
				// CRITICAL FIX: Validate that stored index is recent (within 5 seconds) to handle network ordering issues
				if (skillIndexTimestamp.TryGetValue(moveData.unitId, out float timestamp))
				{
					float age = Time.realtimeSinceStartup - timestamp;
					if (age <= 5f) // Use stored index only if recent
					{
						return storedIndex;
					}
				}
			}
			
			// Fall back to animator's current skill index
			int currentIndex = unitAnimator.GetCurrentSkillIndex();
			if (currentIndex >= 0)
			{
				return currentIndex;
			}
			
			// Final fallback - try to infer from recent moves or default to 0
			Debug.LogWarning($"{LogTag} Unable to determine skill index for PostImpact move for unit {moveData.unitId}, defaulting to 0");
			return 0;
		}
		
		private void CleanupStaleSkillIndices()
		{
			float currentTime = Time.realtimeSinceStartup;
			var keysToRemove = new System.Collections.Generic.List<string>();
			
			foreach (var kvp in skillIndexTimestamp)
			{
				if (currentTime - kvp.Value > 10f) // Remove entries older than 10 seconds
				{
					keysToRemove.Add(kvp.Key);
				}
			}
			
			foreach (var key in keysToRemove)
			{
				unitSkillIndexForPostImpact.Remove(key);
				skillIndexTimestamp.Remove(key);
			}
		}
		
		public void CleanupUnitSkillTracking(string unitId)
		{
			if (!string.IsNullOrEmpty(unitId))
			{
				unitSkillIndexForPostImpact.Remove(unitId);
				skillIndexTimestamp.Remove(unitId);
			}
		}
		
		// CRITICAL FIX: Proper async movement handling with error handling and occupancy management
		private async UniTaskVoid HandleMoveAsyncWithErrorHandling(Unit unit, Vector2Int destination, float duration, float initialProgress, string moveType, string unitId)
		{
			if (unit == null) return;
			
			Vector2Int sourcePosition = unit.CurrentPosition;
			
			try
			{
				// Clear source occupancy before starting movement
				Board.Instance.ClearOccupied(sourcePosition, unit);
				
				// Perform the movement
				await unit.MoveTo(destination, duration, initialProgress, moveType);
				
				// Set destination occupancy after successful movement
				Board.Instance.SetOccupied(destination, unit);
				
				Debug.Log($"{LogTag} Successfully moved unit {unitId} from {sourcePosition} to {destination} with moveType {moveType}");
			}
			catch (System.OperationCanceledException)
			{
				// Movement was cancelled - restore source occupancy
				Board.Instance.SetOccupied(sourcePosition, unit);
				Debug.Log($"{LogTag} Movement cancelled for unit {unitId}, restored occupancy at {sourcePosition}");
			}
			catch (System.Exception ex)
			{
				// Movement failed - restore source occupancy  
				Board.Instance.SetOccupied(sourcePosition, unit);
				Debug.LogError($"{LogTag} Movement failed for unit {unitId}: {ex.Message}, restored occupancy at {sourcePosition}");
			}
		}

		/// <summary>
		/// Parse server animation state string to UnitState enum
		/// </summary>
		private UnitState ParseAnimationState(string animState)
		{
			if (string.IsNullOrEmpty(animState)) return UnitState.Idle;
			
			return animState.ToLowerInvariant() switch
			{
				"idle" => UnitState.Idle,
				"moving" => UnitState.Moving,
				"attacking" => UnitState.Attacking,
				"windup" => UnitState.WindUp,
				_ => UnitState.Idle // Default fallback
			};
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
			public string difficulty; // Optional: difficulty level for bot games
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

		private void WireRoomHandlers()
		{
			if (room == null)
			{
				Debug.LogWarning("[NetworkManager] WireRoomHandlers called with null room");
				return;
			}

			isRoomOpen = true;
			reconnectAttempts = 0;
			
			// Show player names UI immediately when joining match, even without complete data
			// This ensures instant feedback when launcher closes
			if (HudController.Instance != null)
			{
				// Show with basic info - will be updated when server provides complete data
				string localPlayerName = "You";
				if (AuthManager.Instance != null && !string.IsNullOrEmpty(AuthManager.Instance.UserId))
				{
					localPlayerName = "You";
				}
				HudController.Instance.SetPlayerNames(localPlayerName, "Opponent");
				Debug.Log($"{LogTag} Showed player names UI immediately on room join");
			}

			room.OnMessage("*", (string type) =>
			{
				Debug.Log($"{LogTag} ServerMessage received - type='{type}'");
			});

			room.OnMessage<StateSnapshot>("StateSnapshot", snap =>
			{
				if (verboseNetworkLogging && snap != null)
				{
					LogServerMessage("StateSnapshot", snap, LogTag);
				}
				Debug.Log($"{LogTag} StateSnapshot received - serverTick={snap?.serverTick} startTick={snap?.startTick}");
				if (snap != null)
				{
					if (snap.board != null) 
					{
						SetupBoard(snap.board, false); // Use incremental updates for snapshots
						// Update player names UI if we have board data
						UpdatePlayerNamesUI(snap.playerNames, snap.board);
					}
					// Initialize player mana from snapshot if present
					try
					{
						var pid = AuthManager.Instance != null ? AuthManager.Instance.UserId : null;
						if (!string.IsNullOrEmpty(pid))
						{
							float mana = 0f;
							bool found = false;
							if (snap.playerMana != null && snap.playerMana.TryGetValue(pid, out var dictMana)) { mana = dictMana; found = true; }
							var manaBar = ManaBarUI.Instance != null ? ManaBarUI.Instance : FindFirstObjectByType<ManaBarUI>();
							if (found && manaBar != null) manaBar.SetMana(mana);
							if (found && HudController.Instance != null) HudController.Instance.UpdateMana(pid, mana);
						}
						// Countdown from snapshot
						if (HudController.Instance != null)
						{
							HudController.Instance.SetCountdownFromSnapshot(snap, snap.serverTick, DefaultTickRateMs);
						}
					}
					catch { /* ignore */ }
				}
			});
			room.OnMessage<GameEvent>("GameStart", evt =>
			{
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("GameStart", evt.data, LogTag);
				}
				Debug.Log($"{LogTag} GameStart received - serverTick={evt?.serverTick}");
				if (evt != null && evt.data != null)
				{
					if (evt.data.board != null) 
					{
						SetupBoard(evt.data.board, true); // Full reset for game start
						// Update player names UI when game starts
						UpdatePlayerNamesUI(evt.data.playerNames, evt.data.board);
					}
					// Initialize player mana from GameStart if provided
					try
					{
						var pid = AuthManager.Instance != null ? AuthManager.Instance.UserId : null;
						if (!string.IsNullOrEmpty(pid) && evt.data.playerMana != null && evt.data.playerMana.TryGetValue(pid, out var mana))
						{
							var manaBar = ManaBarUI.Instance != null ? ManaBarUI.Instance : FindFirstObjectByType<ManaBarUI>();
							if (manaBar != null) manaBar.SetMana(mana);
							if (HudController.Instance != null) HudController.Instance.UpdateMana(pid, mana);
						}
						// Set max from constants if fetched earlier
    if (!string.IsNullOrEmpty(RulesHash))
    {
        var manaBar = ManaBarUI.Instance != null
            ? ManaBarUI.Instance
            : FindFirstObjectByType<ManaBarUI>();
        if (manaBar != null && lastConfigJson != null)
        {
            var cfg = JsonUtility.FromJson<CombinedConfig>(lastConfigJson);
            if (cfg != null && cfg.constants != null)
            {
                int maxMana = GetMaxManaFromConstants(cfg.constants);
                manaBar.SetMaxPips(maxMana);
            }
        }
    }						// Countdown should stop at game start
						if (HudController.Instance != null) HudController.Instance.StopCountdown();
					}
					catch { /* ignore */ }
				}
			});
			room.OnMessage<GameEvent>("GameAboutToStart", evt =>
			{
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("GameAboutToStart", evt.data, LogTag);
				}
				Debug.Log($"{LogTag} GameAboutToStart received - serverTick={evt?.serverTick} startTick={evt?.data?.startTick}");
				if (evt != null && evt.data != null)
				{
					if (evt.data.board != null) 
					{
						SetupBoard(evt.data.board, true); // Full reset for game about to start
						// Update player names UI when match is about to start
						UpdatePlayerNamesUI(evt.data.playerNames, evt.data.board);
					}
					// Start countdown if we have a future startTick
					try
					{
						if (HudController.Instance != null)
						{
							HudController.Instance.SetCountdownFromGameEvent(evt, DefaultTickRateMs);
						}
					}
					catch { /* ignore */ }
				}
			});
			room.OnMessage<MoveEvent>("Move", evt => { if (evt != null && evt.data != null) { HandleMove(evt.data); if (!string.IsNullOrEmpty(evt.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(evt.intentId); } });
			room.OnMessage<ManaUpdateData>("ManaUpdate", data =>
			{
				try
				{
					var playerId = data != null ? data.playerId : null;
					var mana = data != null ? data.mana : 0f;
					if (verboseNetworkLogging)
					{
						LogServerMessage("ManaUpdate", data, LogTag);
						Debug.Log($"{LogTag} ManaUpdate playerId={playerId} mana={mana}");
					}
					// Mirror JS leniency: if playerId is missing, assume it's for the local player
					bool applyToLocal = false;
					var myId = AuthManager.Instance != null ? AuthManager.Instance.UserId : null;
					if (string.IsNullOrEmpty(playerId))
					{
						applyToLocal = true;
					}
					else if (!string.IsNullOrEmpty(myId) && string.Equals(playerId, myId, StringComparison.Ordinal))
					{
						applyToLocal = true;
					}
					else if (string.IsNullOrEmpty(myId))
					{
						// If we don't know our user id yet, still apply to keep UI responsive
						applyToLocal = true;
					}
					if (applyToLocal)
					{
						var manaBar = ManaBarUI.Instance != null ? ManaBarUI.Instance : FindFirstObjectByType<ManaBarUI>();
						if (manaBar != null) manaBar.SetMana(mana);
						if (HudController.Instance != null) HudController.Instance.UpdateMana(myId ?? playerId ?? string.Empty, mana);
					}
				}
				catch (System.Exception) { /* ignore */ }
			});

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
						var move = DeserializeEventData<MoveData>(envelope.data);
						if (move != null) 
						{ 
							if (verboseNetworkLogging)
							{
								// Create complete move data for logging
								var completeMove = new MoveData
								{
									unitId = move.unitId,
									from = move.from,
									to = move.to,
									startTick = move.startTick,
									endTick = move.endTick,
									currentPips = move.currentPips,
									intentId = envelope.intentId ?? string.Empty,
									serverTick = envelope.serverTick
								};
								LogServerMessage("Move", completeMove, LogTag);
							}
							HandleMove(move); 
							if (!string.IsNullOrEmpty(envelope.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(envelope.intentId); 
						}
						break;
					case "UseSkill":
						var use = DeserializeEventData<UseSkillData>(envelope.data);
						if (use != null && !string.IsNullOrEmpty(use.unitId))
						{
							// CRITICAL FIX: Track skill index for PostImpact moves with timestamp to handle network reordering
							unitSkillIndexForPostImpact[use.unitId] = use.skillId;
							skillIndexTimestamp[use.unitId] = Time.realtimeSinceStartup;
							
							if (verboseNetworkLogging)
							{
								// Create complete use skill data for logging
								var completeUse = new UseSkillData
								{
									unitId = use.unitId,
									skillId = use.skillId,
									origin = use.origin,
									target = use.target,
									startTick = use.startTick,
									endWindupTick = use.endWindupTick,
									hitTick = use.hitTick,
									currentPips = use.currentPips,
									intentId = envelope.intentId ?? string.Empty,
									serverTick = envelope.serverTick
								};
								LogServerMessage("UseSkill", completeUse, LogTag);
							}
							var unit = GameManager.Instance.GetUnitById(use.unitId);
							if (unit != null)
							{
								unit.PlayUseSkill(use).Forget();
								if (VfxManager.Instance != null) VfxManager.Instance.OnUseSkill(use, envelope.serverTick);
							}
						}
						break;
					case "UseSkillResult":
						var res = DeserializeEventData<UseSkillResultData>(envelope.data);
						if (res != null && res.targets != null)
						{
							if (verboseNetworkLogging)
							{
								// Add envelope data for complete logging to match JS client exactly
								if (!string.IsNullOrEmpty(envelope.intentId)) res.intentId = envelope.intentId;
								if (envelope.serverTick > 0) res.serverTick = envelope.serverTick;
								LogServerMessage("UseSkillResult", res, LogTag);
							}
							for (int i = 0; i < res.targets.Length; i++)
							{
								var t = res.targets[i];
								if (t == null) continue;
								var victim = !string.IsNullOrEmpty(t.unitId) ? GameManager.Instance.GetUnitById(t.unitId) : null;
								if (victim != null)
								{
									if (t.damage > 0) victim.ShowDamageNumber(t.damage);
									victim.PlayHitFlash();
                                    // Play GetHit animation only from ServerEvent path (avoid duplicates)
                                    try { var ua = victim.GetComponent<UnitAnimator>(); if (ua != null && Application.isPlaying) ua.PlayGetHit(); } catch { }
									int nextHp = t.hp > 0 ? t.hp : Mathf.Max(0, victim.CurrentHp - Mathf.Max(0, t.damage));
									victim.ApplyHpUpdate(nextHp);
									if (t.killed || t.dead || nextHp <= 0)
									{
										// Instead of immediate destruction, trigger death animation
										victim.Die();
									}
								}
							}
							if (VfxManager.Instance != null) VfxManager.Instance.OnUseSkillResult(res, envelope.serverTick);
							// Do not update player mana from per-unit pips here; rely on ManaUpdate only
							if (!string.IsNullOrEmpty(envelope.intentId) && IntentManager.Instance != null) IntentManager.Instance.HandleIntentResponse(envelope.intentId);
						}
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
			room.OnMessage<UseSkillEvent>("UseSkill", evt =>
			{
				// In play mode, prefer ServerEvent-only handling to avoid duplicates
				if (Application.isPlaying) return;
				try
				{
					var use = evt != null ? evt.data : null;
					if (use != null && !string.IsNullOrEmpty(use.unitId))
					{
						var unit = GameManager.Instance.GetUnitById(use.unitId);
						if (unit != null)
						{
							unit.PlayUseSkill(use).Forget();
							if (VfxManager.Instance != null) VfxManager.Instance.OnUseSkill(use, evt.serverTick);
						}
					}
				}
				catch (System.Exception) { /* ignore */ }
			});
			room.OnMessage<UseSkillResultEvent>("UseSkillResult", evt =>
			{
				// In play mode, prefer ServerEvent-only handling to avoid duplicates
				if (Application.isPlaying) return;
				try
				{
					var res = evt != null ? evt.data : null;
					if (res != null)
					{
						// Show hit feedback
						if (res.targets != null)
						{
							for (int i = 0; i < res.targets.Length; i++)
							{
								var t = res.targets[i];
								if (t == null) continue;
								var victim = !string.IsNullOrEmpty(t.unitId) ? GameManager.Instance.GetUnitById(t.unitId) : null;
								if (victim != null)
								{
									if (t.damage > 0) victim.ShowDamageNumber(t.damage);
									victim.PlayHitFlash();
								}
							}
						}
						if (VfxManager.Instance != null) VfxManager.Instance.OnUseSkillResult(res, evt.serverTick);
					}
				}
				catch { /* ignore */ }
			});
			room.OnMessage<StatusUpdateEvent>("StatusUpdate", evt =>
			{
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("StatusUpdate", evt.data, LogTag);
				}
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
			room.OnMessage<GameOverEvent>("GameOver", evt =>
			{
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("GameOver", evt.data, LogTag);
				}
				
				// Parse end game reason from server
				EndGameReason? endGameReason = null;
				if (evt?.data?.reason != null)
				{
					endGameReason = EndGameReasonExtensions.FromInt(evt.data.reason);
					if (endGameReason == null)
					{
						Debug.LogWarning($"{LogTag} Unknown end game reason received from server: {evt.data.reason}");
					}
				}
				
				// Log detailed GameOver info
				string userResults = "";
				if (evt?.data?.users != null && evt.data.users.Length > 0)
				{
					var results = System.Array.ConvertAll(evt.data.users, u => $"{u.userId}:{u.result}");
					userResults = $" users=[{string.Join(", ", results)}]";
				}
				
				Debug.Log($"{LogTag} GameOver: reason={evt?.data?.reason} ({endGameReason?.GetDescription() ?? "Unknown"}) winner={evt?.data?.winnerUserId} loser={evt?.data?.loserUserId}{userResults}");
				
				if (HudController.Instance != null)
				{
					// Mirror JS client: hide countdown on game over
					HudController.Instance.StopCountdown();
					HudController.Instance.ShowGameOver(evt?.data?.winnerUserId, endGameReason, evt?.data);
				}
			});
			room.OnMessage<ErrorEvent>("Error", evt =>
			{
				if (evt == null || evt.data == null)
				{
					if (verboseNetworkLogging)
					{
						LogServerMessage("Error", new { msg = "Error (no data)" }, LogTag);
					}
					Debug.LogWarning($"{LogTag} Error (no data)");
					if (HudController.Instance != null)
					{
						// Mirror JS client: hide countdown on error
						HudController.Instance.StopCountdown();
						HudController.Instance.ShowToast("Error", "error", 3);
					}
				}
				else
				{
					if (verboseNetworkLogging)
					{
						LogServerMessage("Error", evt.data, LogTag);
					}
					Debug.LogWarning($"{LogTag} Error: code={evt.data.code} msg={evt.data.msg} iid={evt.data.iid} retry={evt.data.retry}");
					if (IntentManager.Instance != null)
					{
						IntentManager.Instance.HandleIntentError(evt);
					}
					if (HudController.Instance != null)
					{
						var msg = string.IsNullOrEmpty(evt.data.msg) ? evt.data.code : evt.data.msg;
						// Mirror JS client: hide countdown on error
						HudController.Instance.StopCountdown();
						HudController.Instance.ShowToast(msg, "error", 3);
					}
				}
			});
			room.OnMessage<ValidTargetsEvent>("ValidTargets", evt =>
			{
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("ValidTargets", evt.data, LogTag);
				}
				int count = evt?.data?.targets != null ? evt.data.targets.Length : 0;
				Debug.Log($"{LogTag} ValidTargets received for unit={evt?.data?.unitId} count={count}");
				OnValidTargets?.Invoke(evt);
			});
			room.OnMessage<UnitDiedEvent>("UnitDied", evt =>
			{
				if (evt?.data?.unitId == null) return;
				if (verboseNetworkLogging && evt?.data != null)
				{
					LogServerMessage("UnitDied", evt.data, LogTag);
				}
				Debug.Log($"{LogTag} UnitDied received for unitId={evt.data.unitId} killerId={evt.data.killerId}");
				var unit = GameManager.Instance.GetUnitById(evt.data.unitId);
				if (unit != null)
				{
					unit.Die(); // Trigger death animation and cleanup
				}
			});

			// Handle disconnections/errors to support auto-reconnect
			room.OnLeave += code =>
			{
				isRoomOpen = false;
				Debug.LogWarning($"{LogTag} Room closed/left. code={code}");
				TryAutoReconnect().Forget();
			};
			room.OnError += (code, message) =>
			{
				Debug.LogWarning($"{LogTag} Room error code={code} message={message}");
				// Treat errors similarly to close and attempt reconnect
				isRoomOpen = false;
				TryAutoReconnect().Forget();
			};
		}

		private async UniTaskVoid TryAutoReconnect()
		{
			// Atomic coalescing to ensure only one reconnect routine runs at a time
			if (Interlocked.CompareExchange(ref reconnectingGate, ReconnectingTrue, ReconnectingFalse) != ReconnectingFalse) return;
			isReconnecting = true;
			
			// Create and link cancellation token source
			_reconnectCts?.Cancel();
			_reconnectCts?.Dispose();
			_reconnectCts = new CancellationTokenSource();
			
			try
			{
				if (colyseusClient == null || lastReconnectionToken == null)
				{
					Debug.LogWarning($"{LogTag} Cannot auto-reconnect: missing client or token");
					return;
				}
				float delay = ReconnectInitialDelaySeconds;
				while (reconnectAttempts < ReconnectMaxAttempts)
				{
					_reconnectCts.Token.ThrowIfCancellationRequested();
					reconnectAttempts++;
					Debug.Log($"{LogTag} Attempting reconnect {reconnectAttempts}/{ReconnectMaxAttempts} after {delay:0.##}s...");
					await UniTask.Delay((int)(delay * 1000f), cancellationToken: _reconnectCts.Token);
					try
					{
						await Reconnect();
						isRoomOpen = true;
						Debug.Log($"{LogTag} Reconnect successful.");
						if (HudController.Instance != null) HudController.Instance.ShowToast("Reconnected", "info", 2);
						return;
					}
					catch (System.Exception ex)
					{
						Debug.LogWarning($"{LogTag} Reconnect attempt {reconnectAttempts} failed: {ex.Message}");
					}
					delay = Mathf.Min(delay * 2f, ReconnectMaxDelaySeconds);
				}
				Debug.LogWarning($"{LogTag} Max reconnect attempts reached. Please try manually.");
				if (HudController.Instance != null) HudController.Instance.ShowToast("Disconnected", "error", 3);
			}
			catch (OperationCanceledException)
			{
				Debug.Log($"{LogTag} Reconnection cancelled");
			}
			finally
			{
				isReconnecting = false;
				Volatile.Write(ref reconnectingGate, ReconnectingFalse);
			}
		}
	}
}
