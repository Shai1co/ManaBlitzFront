using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;
using UnityEngine;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.VFX;
#endif

namespace ManaGambit
{
	[ExecuteAlways]
	public class VfxManager : MonoBehaviour
	{
		private const float MinProjectileTravelSeconds = 0.001f;
		private const float MinLookDirMagnitudeSqr = 0.0001f;
		public static VfxManager Instance { get; private set; }

		[SerializeField] private SkillVfxDatabase database;
		[SerializeField] private bool autoPlayParticles = true;

		// Remember last intended target position per attacker (unitId)
		private readonly Dictionary<string, Vector3> attackerIdToTargetPos = new Dictionary<string, Vector3>();
		private readonly Dictionary<string, GameObject> attackerIdToWindup = new Dictionary<string, GameObject>();
		private readonly Dictionary<string, CancellationTokenSource> attackerIdToCts = new Dictionary<string, CancellationTokenSource>();
		private readonly Dictionary<string, float> attackerIdToOriginalYaw = new Dictionary<string, float>();
		// Remember pieceId per attacker so we can resolve presets on result even if the Unit lookup fails
		private readonly Dictionary<string, string> attackerIdToPieceId = new Dictionary<string, string>();

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				if (Application.isPlaying) Destroy(this);
				else DestroyImmediate(this);
				return;
			}
			Instance = this;
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void EditorAutoInit()
		{
			EditorEnsureInstance();
		}

		public static void EditorEnsureInstance()
		{
			if (Instance == null)
			{
				var found = UnityEngine.Object.FindFirstObjectByType<VfxManager>(UnityEngine.FindObjectsInactive.Include);
				if (found != null) Instance = found;
			}
		}
#endif

		public SkillVfxPreset GetPresetByActionName(string actionName)
		{
			return database != null ? database.GetByActionName(actionName) : null;
		}

		public SkillVfxPreset GetPresetByPieceAndIndex(string pieceId, int actionIndex, UnitConfig unitConfig)
		{
			if (unitConfig == null) return null;
			var actionName = unitConfig.GetActionName(pieceId, actionIndex);
			if (string.IsNullOrEmpty(actionName)) return null;
			return GetPresetByActionName(actionName);
		}

		public GameObject PlayAt(Transform parent, GameObject prefab, bool lookAtCamera = false)
		{
			if (prefab == null || parent == null) return null;
			var rotation = lookAtCamera && Camera.main != null ? Camera.main.transform.rotation : parent.rotation;
			var go = Instantiate(prefab, parent.position, rotation, parent);
			if (autoPlayParticles) AutoPlay(go);
			return go;
		}

		public GameObject PlayAt(Vector3 position, Quaternion rotation, GameObject prefab, bool lookAtCamera = false)
		{
			if (prefab == null) return null;
			if (lookAtCamera && Camera.main != null)
			{
				rotation = Camera.main.transform.rotation;
			}
			var go = Instantiate(prefab, position, rotation);
			if (autoPlayParticles) AutoPlay(go);
			return go;
		}

		public async UniTask PlayProjectile(Transform source, Vector3 targetPos, GameObject projectilePrefab, int travelMs, bool lookAtCamera, float arcDegrees, CancellationToken token)
		{
			if (projectilePrefab == null || source == null) return;
			float travelSeconds = Mathf.Max(MinProjectileTravelSeconds, travelMs / 1000f);
			Vector3 startPos = source.position;
			float arcRad = Mathf.Deg2Rad * Mathf.Max(0f, arcDegrees);
			float halfDist = 0.5f * Vector3.Distance(startPos, targetPos);
			float apexHeight = Mathf.Tan(arcRad) * halfDist;
			Quaternion rotation;
			if (lookAtCamera && Camera.main != null)
			{
				rotation = Camera.main.transform.rotation;
			}
			else
			{
				Vector3 direction = (targetPos - startPos).normalized;
				rotation = direction == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(direction);
			}
			var proj = Instantiate(projectilePrefab, startPos, rotation);
			if (autoPlayParticles) AutoPlay(proj);
			float elapsed = 0f;
			while (elapsed < travelSeconds)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, token);
				if (proj == null || proj.transform == null) break;
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / travelSeconds);
				Vector3 basePos = Vector3.Lerp(startPos, targetPos, t);
				float h = apexHeight * Mathf.Sin(Mathf.PI * t);
				Vector3 pos = basePos + (Vector3.up * h);
				proj.transform.position = pos;
				if (!lookAtCamera)
				{
					float nextT = Mathf.Clamp01(t + (Time.deltaTime / travelSeconds));
					Vector3 nextBase = Vector3.Lerp(startPos, targetPos, nextT);
					float nextH = apexHeight * Mathf.Sin(Mathf.PI * nextT);
					Vector3 nextPos = nextBase + (Vector3.up * nextH);
					Vector3 vel = nextPos - pos;
					if (vel.sqrMagnitude > 0.0000001f)
					{
						proj.transform.rotation = Quaternion.LookRotation(vel.normalized);
					}
				}
			}
			if (proj != null) Destroy(proj);
		}

		private async UniTaskVoid ScheduleProjectileAfterDelay(Transform source, Vector3 targetPos, GameObject projectilePrefab, int launchDelayMs, int travelMs, bool lookAtCamera, float arcDegrees, CancellationToken token)
		{
			try { await UniTask.Delay(launchDelayMs, cancellationToken: token).SuppressCancellationThrow(); }
			catch { }
			await PlayProjectile(source, targetPos, projectilePrefab, travelMs, lookAtCamera, arcDegrees, token);
		}

		private void AutoPlay(GameObject go)
		{
			if (go == null) return;
			try
			{
				var pss = go.GetComponentsInChildren<ParticleSystem>(true);
				for (int i = 0; i < pss.Length; i++)
				{
					var ps = pss[i];
					if (ps == null) continue;
					ps.Clear(true);
					ps.Play(true);
				}
#if UNITY_2019_1_OR_NEWER
				var vfx = go.GetComponentsInChildren<VisualEffect>(true);
				for (int i = 0; i < vfx.Length; i++)
				{
					if (vfx[i] != null) vfx[i].Play();
				}
#endif
			}
			catch { }
		}

		public void OnUseSkill(UseSkillData use, int serverTick)
		{
			if (use == null || GameManager.Instance == null) return;
			var attacker = GameManager.Instance.GetUnitById(use.unitId);
			if (attacker == null) return;
			// Remember pieceId for this attacker to handle cases where the unit lookup might be unavailable on result
			if (!string.IsNullOrEmpty(attacker.UnitID)) attackerIdToPieceId[attacker.UnitID] = attacker.PieceId;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var preset = cfg != null ? GetPresetByPieceAndIndex(attacker.PieceId, Mathf.Max(0, use.skillId), cfg) : null;
			if (preset == null) return;

			int delayMs = 0;
			if (use.hitTick > 0 && serverTick > 0 && use.hitTick > serverTick)
			{
				int dt = use.hitTick - serverTick;
				delayMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
			}

			int windupDurationMs = 0;
			if (preset.WindupPolicy == SkillVfxPreset.WindupDurationPolicy.FixedMs)
			{
				windupDurationMs = Mathf.Max(0, preset.FixedWindupMs);
			}
			else
			{
				if (use.startTick > 0 && use.endWindupTick > use.startTick)
				{
					int dt = use.endWindupTick - use.startTick;
					windupDurationMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
				}
				if (windupDurationMs <= 0) windupDurationMs = delayMs;
			}

			var attackerAnchors = attacker.GetComponent<UnitVfxAnchors>();
			Transform windupAnchor = attackerAnchors != null
				? attackerAnchors.FindSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName)
				: attacker.transform;
			Transform projectileAnchor = attackerAnchors != null
				? attackerAnchors.FindSourceAnchor(preset.ProjectileAttach, preset.CustomSourceAnchorName)
				: attacker.transform;

			Vector3 targetWorldPos = attacker.transform.position;
			if (use.target != null && use.target.cell != null)
			{
				var cell = new Vector2Int(use.target.cell.x, use.target.cell.y);
				// Resolve precise target point per preset
				if (preset.ProjectileTarget == SkillVfxPreset.TargetAnchor.TargetImpactAnchor || preset.ProjectileTarget == SkillVfxPreset.TargetAnchor.Custom || preset.ProjectileTarget == SkillVfxPreset.TargetAnchor.TargetRoot)
				{
					var targetUnit = GameManager.Instance.GetUnitById(use.target.unitId);
					if (targetUnit != null)
					{
						var ta = targetUnit.GetComponent<UnitVfxAnchors>();
						Transform tAnchor = null;
						switch (preset.ProjectileTarget)
						{
							case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
								tAnchor = ta != null ? ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.TargetImpactAnchor, preset.CustomTargetAnchorName) : null;
								break;
							case SkillVfxPreset.TargetAnchor.TargetRoot:
								tAnchor = targetUnit.transform;
								break;
							case SkillVfxPreset.TargetAnchor.Custom:
								tAnchor = ta != null ? ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.Custom, preset.CustomTargetAnchorName) : null;
								break;
						}
						if (tAnchor != null)
						{
							targetWorldPos = tAnchor.position;
						}
						else
						{
							targetWorldPos = Board.Instance.GetSlotWorldPosition(cell);
						}
					}
					else
					{
						targetWorldPos = Board.Instance.GetSlotWorldPosition(cell);
					}
				}
				else
				{
					targetWorldPos = Board.Instance.GetSlotWorldPosition(cell);
				}
			}
			RememberTargetPosition(attacker.UnitID, targetWorldPos);

			if (attackerIdToCts.TryGetValue(attacker.UnitID, out var existingCts))
			{
				existingCts.Cancel();
				existingCts.Dispose();
				attackerIdToCts.Remove(attacker.UnitID);
			}
			var cts = new CancellationTokenSource();
			attackerIdToCts[attacker.UnitID] = cts;
			var linked = CancellationTokenSource.CreateLinkedTokenSource(attacker.GetCancellationTokenOnDestroy(), cts.Token);

			// Face target on Y only, then restore after max(windup, delay)
			try
			{
				float originalYaw = attacker.transform.eulerAngles.y;
				attackerIdToOriginalYaw[attacker.UnitID] = originalYaw;
				YawLookAt(attacker, targetWorldPos);
				int restoreMs = Mathf.Max(1, Mathf.Max(windupDurationMs, delayMs));
				RestoreYawAfterDelay(attacker.UnitID, originalYaw, restoreMs, linked.Token).Forget();
			}
			catch { }

			RunWindupAndProjectile(attacker.UnitID, preset, windupAnchor, projectileAnchor, windupDurationMs, delayMs, targetWorldPos, linked.Token).Forget();

			// Resolve UnitConfig action now for follow-up effects
			UnitConfig.ActionInfo action = null;
			if (cfg != null)
			{
				var dataForAction = cfg.GetData(attacker.PieceId);
				int sidxForAction = Mathf.Max(0, use.skillId);
				if (dataForAction != null && dataForAction.actions != null && sidxForAction >= 0 && sidxForAction < dataForAction.actions.Length)
				{
					action = dataForAction.actions[sidxForAction];
				}
			}

			// Multi-shot scheduling based on UnitConfig attack.amount/interval (visual only)
			int numShots = 1;
			int shotIntervalMs = 0;
			try
			{
				if (action != null && action.attack != null)
				{
					numShots = Mathf.Max(1, action.attack.amount);
					shotIntervalMs = Mathf.Max(0, action.attack.interval);
				}
			}
			catch { numShots = 1; shotIntervalMs = 0; }
			if (numShots > 1 && preset.ProjectilePrefab != null && projectileAnchor != null)
			{
				int travelMsExtra = 0;
				if (preset.TravelMs > 0)
				{
					travelMsExtra = preset.TravelMs;
				}
				else if (preset.ProjectileSpeedUnitsPerSec > 0f)
				{
					float distance = Vector3.Distance(projectileAnchor.position, targetWorldPos);
					float seconds = Mathf.Max(MinProjectileTravelSeconds, distance / preset.ProjectileSpeedUnitsPerSec);
					travelMsExtra = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));
				}
				if (travelMsExtra > 0)
				{
					for (int s = 1; s < numShots; s++)
					{
						int sDelay = delayMs + (s * shotIntervalMs);
						int launchDelayMs = Mathf.Max(0, sDelay - travelMsExtra);
						ScheduleProjectileAfterDelay(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, launchDelayMs, travelMsExtra, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, linked.Token).Forget();
						// Replay the skill animation for each additional shot at projectile launch time
						try
						{
							var ua = attacker.GetComponent<UnitAnimator>();
							int skillIdxForAnim = Mathf.Max(0, use.skillId);
							if (ua != null)
							{
								UniTask.Void(async () => { try { await UniTask.Delay(launchDelayMs, cancellationToken: linked.Token); ua.PlaySkillShotNow(skillIdxForAnim); } catch { } });
							}
						}
						catch { }
					}
				}
			}

			// Schedule skill end event after last shot reaches target
			try
			{
				var attackerUnit = attacker;
				if (attackerUnit != null)
				{
					int skillIdx = 0;
					try { var cfg2 = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null; skillIdx = Mathf.Max(0, cfg2 != null ? cfg2.GetActionIndexByName(attackerUnit.PieceId, preset.ActionName) : 0); } catch { }
					int totalMs = Mathf.Max(windupDurationMs, delayMs);
					if (numShots > 1)
					{
						// add extra interval time beyond first shot
						totalMs = Mathf.Max(totalMs, delayMs + (numShots - 1) * shotIntervalMs);
					}
					// Add an estimated projectile travel for end alignment when needed
					int estTravelMs = 0;
					if (preset.ProjectilePrefab != null)
					{
						if (preset.TravelMs > 0) estTravelMs = preset.TravelMs; else if (preset.ProjectileSpeedUnitsPerSec > 0f)
						{
							float dist = Vector3.Distance(projectileAnchor != null ? projectileAnchor.position : attackerUnit.transform.position, targetWorldPos);
							float sec = Mathf.Max(MinProjectileTravelSeconds, dist / Mathf.Max(0.001f, preset.ProjectileSpeedUnitsPerSec));
							estTravelMs = Mathf.Max(0, Mathf.RoundToInt(sec * 1000f));
						}
					}
					int endInMs = totalMs + estTravelMs;
					var go = attackerUnit.gameObject;
					UniTask.Void(async () => { try { await UniTask.Delay(endInMs, cancellationToken: linked.Token); go.SendMessage("InvokeEndForSkillIndex", skillIdx, SendMessageOptions.DontRequireReceiver); } catch { } });
					// Also force a return-home animation if the UnitConfig postImpact is ReturnHome
					try
					{
						var cfg3 = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
						if (cfg3 != null && attackerUnit != null)
						{
							var data = cfg3.GetData(attackerUnit.PieceId);
							if (data != null && data.actions != null && skillIdx >= 0 && skillIdx < data.actions.Length)
							{
								var actionInfo = data.actions[skillIdx];
								string beh = actionInfo?.move?.postImpact?.behavior;
								if (!string.IsNullOrEmpty(beh) && beh.Replace(" ", string.Empty).Equals("returnhome", System.StringComparison.OrdinalIgnoreCase))
								{
									var ua3 = attackerUnit.GetComponent<UnitAnimator>();
									if (ua3 != null) ua3.PlayReturnHomeState(skillIdx);
								}
							}
						}
					}
					catch { }
				}
			}
			catch { }

			// Spawn aura if the preset has one; duration mirrors UnitConfig aura if available
			int auraDurationMs = Mathf.Max(0, action?.aura?.duration ?? 0);
			if (preset.AuraPrefab != null && auraDurationMs > 0)
			{
				Transform auraAnchor = attackerAnchors != null
					? attackerAnchors.FindSourceAnchor(preset.AuraAttach, preset.CustomSourceAnchorName)
					: attacker.transform;
				var auraGo = PlayAt(auraAnchor, preset.AuraPrefab, preset.AuraLookAtCamera);
				if (auraGo != null)
				{
#if UNITY_EDITOR
					if (!Application.isPlaying)
					{
						RegisterEditorParticles(auraGo);
						int ms = EstimateImpactLifetimeMs(preset.AuraPrefab);
						ScheduleEditorDestroy(auraGo, ms);
					}
					else
#endif
					{
						int ms = EstimateImpactLifetimeMs(preset.AuraPrefab);
						Destroy(auraGo, Mathf.Max(0.01f, ms / 1000f));
					}
				}
			}

			// Spawn buff if the preset has one; duration mirrors UnitConfig buff if available
			int buffDurationMs = Mathf.Max(0, action?.buff?.duration ?? 0);
			if (preset.BuffPrefab != null && buffDurationMs > 0)
			{
				// Buff targets are determined by UnitConfig, but for visual effects we spawn on attacker by default
				// unless the buff attachment specifies otherwise
				Transform buffAnchor = attacker.transform;
				switch (preset.BuffAttach)
				{
					case SkillVfxPreset.TargetAnchor.AttackerRoot:
						buffAnchor = attackerAnchors != null
							? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.Root, preset.CustomSourceAnchorName)
							: attacker.transform;
						break;
					case SkillVfxPreset.TargetAnchor.TargetRoot:
					case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
					case SkillVfxPreset.TargetAnchor.Custom:
						// For target-based buffs, we need target information which comes later in OnUseSkillResult
						// For now, spawn on attacker as fallback
						buffAnchor = attacker.transform;
						break;
					case SkillVfxPreset.TargetAnchor.WorldAtTargetCell:
					case SkillVfxPreset.TargetAnchor.AttackerProjectileAnchor:
					default:
						buffAnchor = attacker.transform;
						break;
				}
				var buffGo = PlayAt(buffAnchor, preset.BuffPrefab, preset.BuffLookAtCamera);
				if (buffGo != null)
				{
					Destroy(buffGo, Mathf.Max(0.01f, buffDurationMs / 1000f));
				}
			}
		}

		private void YawLookAt(Unit attacker, Vector3 targetWorldPos)
		{
			if (attacker == null) return;
			Vector3 lookDelta = targetWorldPos - attacker.transform.position;
			lookDelta.y = 0f;
			if (lookDelta.sqrMagnitude > MinLookDirMagnitudeSqr)
			{
				float yaw = Mathf.Atan2(lookDelta.x, lookDelta.z) * Mathf.Rad2Deg;
				var e = attacker.transform.eulerAngles;
				e.y = yaw;
				attacker.transform.eulerAngles = e;
			}
		}

		private async UniTaskVoid RestoreYawAfterDelay(string unitId, float originalYaw, int delayMs, CancellationToken token)
		{
			try { await UniTask.Delay(delayMs, cancellationToken: token); }
			catch { }
			var u = GameManager.Instance != null ? GameManager.Instance.GetUnitById(unitId) : null;
			if (u != null)
			{
				var e = u.transform.eulerAngles;
				e.y = originalYaw;
				u.transform.eulerAngles = e;
			}
			attackerIdToOriginalYaw.Remove(unitId);
		}

		private async UniTaskVoid RunWindupAndProjectile(
			string attackerUnitId,
			SkillVfxPreset preset,
			Transform windupAnchor,
			Transform projectileAnchor,
			int windupDurationMs,
			int delayMs,
			Vector3 targetWorldPos,
			CancellationToken token)
		{
			// Wind-up
			if (preset.WindupPrefab != null && windupDurationMs > 0 && windupAnchor != null)
			{
				// Invoke unit skill event for this skill index at windup start
				try
				{
					var attackerUnit = GameManager.Instance != null ? GameManager.Instance.GetUnitById(attackerUnitId) : null;
					if (attackerUnit != null)
					{
						int skillIdx = 0;
						try { var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null; skillIdx = Mathf.Max(0, cfg != null ? cfg.GetActionIndexByName(attackerUnit.PieceId, preset.ActionName) : 0); } catch { }
						attackerUnit.gameObject.SendMessage("InvokeForSkillIndex", skillIdx, SendMessageOptions.DontRequireReceiver);
					}
				}
				catch { }
				var go = PlayAt(windupAnchor, preset.WindupPrefab, preset.WindupLookAtCamera);
				attackerIdToWindup[attackerUnitId] = go;
				if (preset.AudioCast != string.Empty)
				{
					MasterAudio.PlaySound3DAtVector3AndForget(preset.AudioCast, windupAnchor.position);
				}
				try { await UniTask.Delay(windupDurationMs, cancellationToken: token); }
				catch { }
				StopWindup(attackerUnitId);
			}

			// Projectile (spawn even if delayMs == 0 by launching immediately)
			if (preset.ProjectilePrefab != null && projectileAnchor != null)
			{
				int travelMs = 0;
				if (preset.TravelMs > 0)
				{
					travelMs = preset.TravelMs;
				}
				else if (preset.ProjectileSpeedUnitsPerSec > 0f)
				{
					float distance = Vector3.Distance(projectileAnchor.position, targetWorldPos);
					float seconds = Mathf.Max(MinProjectileTravelSeconds, distance / preset.ProjectileSpeedUnitsPerSec);
					travelMs = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));
				}
				if (travelMs > 0)
				{
					if (delayMs > 0)
					{
						int launchDelayMs = Mathf.Max(0, delayMs - travelMs);
						await UniTask.Delay(launchDelayMs, cancellationToken: token).SuppressCancellationThrow();
						await PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, token);
					}
					else
					{
						await PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, token);
					}
				}
			}
		}

		private void StopWindup(string attackerUnitId)
		{
			if (attackerIdToWindup.TryGetValue(attackerUnitId, out var go))
			{
				if (go != null) Destroy(go);
				attackerIdToWindup.Remove(attackerUnitId);
			}
		}

		public void OnUseSkillResult(UseSkillResultData res, int serverTick)
		{
			if (res == null || GameManager.Instance == null) return;
			var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
			var attacker = GameManager.Instance.GetUnitById(res.attacker);
			SkillVfxPreset preset = null;
			if (cfg != null)
			{
				string piece = attacker != null ? attacker.PieceId : null;
				if (string.IsNullOrEmpty(piece)) attackerIdToPieceId.TryGetValue(res.attacker, out piece);
				if (!string.IsNullOrEmpty(piece))
				{
					preset = GetPresetByPieceAndIndex(piece, Mathf.Max(0, res.skillId), cfg);
				}
			}
			if (preset == null || preset.ImpactPrefab == null) return;

			Vector3 rememberedTargetPos;
			bool haveRemembered = TryGetRememberedTargetPosition(res.attacker, out rememberedTargetPos);

			if (res.targets != null)
			{
				for (int i = 0; i < res.targets.Length; i++)
				{
					var t = res.targets[i];
					if (t == null) continue;
					var targetUnit = !string.IsNullOrEmpty(t.unitId) ? GameManager.Instance.GetUnitById(t.unitId) : null;
					Vector3 fallback = targetUnit != null
						? targetUnit.transform.position
						: (haveRemembered ? rememberedTargetPos : attacker != null ? attacker.transform.position : Vector3.zero);
					SpawnImpact(preset, attacker, targetUnit, fallback);
				}
			}
		}

		public void RememberTargetPosition(string attackerUnitId, Vector3 worldPos)
		{
			if (string.IsNullOrEmpty(attackerUnitId)) return;
			attackerIdToTargetPos[attackerUnitId] = worldPos;
		}

		public bool TryGetRememberedTargetPosition(string attackerUnitId, out Vector3 worldPos)
		{
			if (!string.IsNullOrEmpty(attackerUnitId) && attackerIdToTargetPos.TryGetValue(attackerUnitId, out worldPos))
			{
				return true;
			}
			worldPos = Vector3.zero;
			return false;
		}

		public void SpawnImpact(SkillVfxPreset preset, Unit attacker, Unit target, Vector3 targetWorldFallback)
		{
			SpawnImpact(preset, attacker, target, targetWorldFallback, null);
		}

		public void SpawnImpact(
			SkillVfxPreset preset,
			Unit attacker,
			Unit target,
			Vector3 targetWorldFallback,
			UnitConfig overrideConfig,
			int overrideActionIndex = -1,
			int overrideBuffDurationMs = -1)
		{
			if (preset == null) return;

			// Spawn impact effect
			if (preset.ImpactPrefab != null)
			{
				// Decide spawn transform
				GameObject spawned = null;
				switch (preset.ImpactAttach)
				{
					case SkillVfxPreset.TargetAnchor.TargetRoot:
					{
						var t = target != null ? target.transform : null;
						if (t != null) spawned = PlayAt(t, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						else spawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
					case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
					{
						Transform anchor = null;
						if (target != null)
						{
							var ta = target.GetComponent<UnitVfxAnchors>();
							if (ta != null)
							{
								anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.TargetImpactAnchor, preset.CustomTargetAnchorName);
							}
						}
						if (anchor != null) spawned = PlayAt(anchor, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						else spawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
					case SkillVfxPreset.TargetAnchor.Custom:
					{
						Transform anchor = null;
						if (target != null)
						{
							var ta = target.GetComponent<UnitVfxAnchors>();
							if (ta != null)
							{
								anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.Custom, preset.CustomTargetAnchorName);
							}
						}
						if (anchor != null) spawned = PlayAt(anchor, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						else spawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
					case SkillVfxPreset.TargetAnchor.TargetColliderClosestPointToAttackerProjectileAnchor:
					{
						Vector3 spawnPos = targetWorldFallback;
						if (target != null)
						{
							// Find attacker's projectile anchor world pos
							Vector3 sourcePos = attacker != null ? attacker.transform.position : targetWorldFallback;
							if (attacker != null)
							{
								var aa = attacker.GetComponent<UnitVfxAnchors>();
								var projA = aa != null ? aa.FindSourceAnchor(SkillVfxPreset.SourceAnchor.ProjectileAnchor, preset.CustomSourceAnchorName) : null;
								if (projA != null) sourcePos = projA.position;
							}

							// Get all colliders on target and compute closest point
							float bestDist = float.PositiveInfinity;
							Vector3 bestPoint = target.transform.position;
							var cols = target.GetComponentsInChildren<Collider>(true);
							for (int i = 0; i < cols.Length; i++)
							{
								var c = cols[i];
								if (c == null || !c.enabled) continue;
								Vector3 p = c.ClosestPoint(sourcePos);
								float d2 = (p - sourcePos).sqrMagnitude;
								if (d2 < bestDist)
								{
									bestDist = d2;
									bestPoint = p;
								}
							}
							spawnPos = bestPoint;
						}
						spawned = PlayAt(spawnPos, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
					case SkillVfxPreset.TargetAnchor.AttackerProjectileAnchor:
					{
						Transform anchor = null;
						if (attacker != null)
						{
							var ta = attacker.GetComponent<UnitVfxAnchors>();
							if (ta != null)
							{
								anchor = ta.FindSourceAnchor(SkillVfxPreset.SourceAnchor.ProjectileAnchor, preset.CustomSourceAnchorName);
							}
						}
						if (anchor != null) spawned = PlayAt(anchor.position, anchor.rotation, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						else spawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
					case SkillVfxPreset.TargetAnchor.WorldAtTargetCell:
					default:
					{
						spawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.ImpactPrefab, preset.ImpactLookAtCamera);
						break;
					}
				}
				if (preset.AudioImpact != string.Empty)
				{
					MasterAudio.PlaySound3DAtVector3AndForget(preset.AudioImpact, targetWorldFallback);
				}
#if UNITY_EDITOR
				if (!Application.isPlaying && spawned != null)
				{
					RegisterEditorParticles(spawned);
					int ms = EstimateImpactLifetimeMs(preset.ImpactPrefab);
					ScheduleEditorDestroy(spawned, ms);
				}
#endif
				if (Application.isPlaying && spawned != null)
				{
					int ms = EstimateImpactLifetimeMs(preset.ImpactPrefab);
					Destroy(spawned, Mathf.Max(0.01f, ms / 1000f));
				}
			}

			// Spawn buff effect if preset has one
			if (preset.BuffPrefab != null)
			{
				// Get buff duration from UnitConfig (prefer override if provided)
				int buffDurationMs = overrideBuffDurationMs > 0 ? overrideBuffDurationMs : 0;
				string buffTargetsStr = null;
				var cfg = overrideConfig != null ? overrideConfig : (NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null);
				if (buffDurationMs <= 0 && cfg != null && attacker != null)
				{
					var data = cfg.GetData(attacker.PieceId);
					if (data != null && data.actions != null)
					{
						int sidx = overrideActionIndex;
						if (sidx < 0)
						{
							try { sidx = cfg.GetActionIndexByName(attacker.PieceId, preset.ActionName); } catch { }
						}
						if (sidx >= 0 && sidx < data.actions.Length)
						{
							var action = data.actions[sidx];
							buffDurationMs = Mathf.Max(0, action?.buff?.duration ?? 0);
							buffTargetsStr = action?.buff?.targets;
						}
						else
						{
							for (int i = 0; i < data.actions.Length; i++)
							{
								var action = data.actions[i];
								if (action != null && action.name == preset.ActionName)
								{
									buffDurationMs = Mathf.Max(0, action.buff?.duration ?? 0);
									buffTargetsStr = action?.buff?.targets;
									break;
								}
							}
						}
					}
				}

				int effectiveBuffMs = buffDurationMs > 0 ? buffDurationMs : EstimateImpactLifetimeMs(preset.BuffPrefab);
				if (effectiveBuffMs > 0)
				{
					GameObject buffSpawned = null;
					bool preferAttacker = string.Equals(buffTargetsStr, "self", System.StringComparison.OrdinalIgnoreCase);
					switch (preset.BuffAttach)
					{
						case SkillVfxPreset.TargetAnchor.TargetRoot:
						{
							if (preferAttacker && attacker != null)
							{
								var attackerAnchors = attacker.GetComponent<UnitVfxAnchors>();
								var aroot = attackerAnchors != null
									? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.Root, preset.CustomSourceAnchorName)
									: attacker.transform;
								buffSpawned = PlayAt(aroot, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							else
							{
								var t = target != null ? target.transform : null;
								if (t != null) buffSpawned = PlayAt(t, preset.BuffPrefab, preset.BuffLookAtCamera);
								else buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							break;
						}
						case SkillVfxPreset.TargetAnchor.TargetImpactAnchor:
						{
							if (preferAttacker && attacker != null)
							{
								var attackerAnchors = attacker.GetComponent<UnitVfxAnchors>();
								var aroot = attackerAnchors != null
									? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.Root, preset.CustomSourceAnchorName)
									: attacker.transform;
								buffSpawned = PlayAt(aroot, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							else
							{
								Transform anchor = null;
								if (target != null)
								{
									var ta = target.GetComponent<UnitVfxAnchors>();
									if (ta != null)
									{
										anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.TargetImpactAnchor, preset.CustomTargetAnchorName);
									}
								}
								if (anchor != null) buffSpawned = PlayAt(anchor, preset.BuffPrefab, preset.BuffLookAtCamera);
								else buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							break;
						}
						case SkillVfxPreset.TargetAnchor.Custom:
						{
							if (preferAttacker && attacker != null)
							{
								var attackerAnchors = attacker.GetComponent<UnitVfxAnchors>();
								var aroot = attackerAnchors != null
									? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.Root, preset.CustomSourceAnchorName)
									: attacker.transform;
								buffSpawned = PlayAt(aroot, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							else
							{
								Transform anchor = null;
								if (target != null)
								{
									var ta = target.GetComponent<UnitVfxAnchors>();
									if (ta != null)
									{
										anchor = ta.FindTargetAnchor(SkillVfxPreset.TargetAnchor.Custom, preset.CustomTargetAnchorName);
									}
								}
								if (anchor != null) buffSpawned = PlayAt(anchor, preset.BuffPrefab, preset.BuffLookAtCamera);
								else buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							}
							break;
						}
						case SkillVfxPreset.TargetAnchor.AttackerRoot:
						{
							var attackerAnchors = attacker != null ? attacker.GetComponent<UnitVfxAnchors>() : null;
							Transform anchor = attackerAnchors != null
								? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.Root, preset.CustomSourceAnchorName)
								: (attacker != null ? attacker.transform : null);
							if (anchor != null) buffSpawned = PlayAt(anchor, preset.BuffPrefab, preset.BuffLookAtCamera);
							else buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							break;
						}
						case SkillVfxPreset.TargetAnchor.AttackerProjectileAnchor:
						{
							var attackerAnchors = attacker != null ? attacker.GetComponent<UnitVfxAnchors>() : null;
							Transform anchor = attackerAnchors != null
								? attackerAnchors.FindSourceAnchor(SkillVfxPreset.SourceAnchor.ProjectileAnchor, preset.CustomSourceAnchorName)
								: (attacker != null ? attacker.transform : null);
							if (anchor != null) buffSpawned = PlayAt(anchor.position, anchor.rotation, preset.BuffPrefab, preset.BuffLookAtCamera);
							else buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							break;
						}
						case SkillVfxPreset.TargetAnchor.WorldAtTargetCell:
						default:
						{
							buffSpawned = PlayAt(targetWorldFallback, Quaternion.identity, preset.BuffPrefab, preset.BuffLookAtCamera);
							break;
						}
					}

					// Enforce looping for buff effects so they persist visually for the full duration
					// For reliability, force-loop regardless of the preset flag when a positive duration is defined
					if (buffSpawned != null)
					{
						try
						{
							var pss = buffSpawned.GetComponentsInChildren<ParticleSystem>(true);
							for (int i = 0; i < pss.Length; i++)
							{
								var ps = pss[i];
								if (ps == null) continue;
								var main = ps.main;
								main.loop = true;
								main.stopAction = ParticleSystemStopAction.None;
								ps.Clear(true);
								ps.Play(true);
							}
#if UNITY_2019_1_OR_NEWER
							var vfx = buffSpawned.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true);
							for (int i = 0; i < vfx.Length; i++)
							{
								if (vfx[i] != null) vfx[i].Play();
							}
#endif
						}
						catch { }
					}

#if UNITY_EDITOR
					Debug.Log($"[VfxManager] Buff spawn: action='{preset.ActionName}', attach='{preset.BuffAttach}', durationMs={effectiveBuffMs}, preferAttacker={preferAttacker}, attacker={(attacker!=null?attacker.name:"null")}, target={(target!=null?target.name:"null")}");
					if (!Application.isPlaying && buffSpawned != null)
					{
						RegisterEditorParticles(buffSpawned);
						ScheduleEditorDestroy(buffSpawned, effectiveBuffMs);
					}
#endif
					if (buffSpawned != null && Application.isPlaying)
					{
						// Ensure the buff persists for the full resolved duration
						Destroy(buffSpawned, Mathf.Max(0.01f, effectiveBuffMs / 1000f));
					}
				}
			}
		}

		private int EstimateImpactLifetimeMs(GameObject prefab)
		{
			if (prefab == null) return 2000;
			float maxSeconds = 0f;
			try
			{
				var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
				for (int i = 0; i < systems.Length; i++)
				{
					var ps = systems[i];
					if (ps == null) continue;
					var main = ps.main;
					if (main.loop)
					{
						maxSeconds = Mathf.Max(maxSeconds, 2.0f);
						continue;
					}
					float lifetime = GetCurveMax(main.startLifetime);
					float duration = main.duration + lifetime;
					maxSeconds = Mathf.Max(maxSeconds, duration);
				}
			}
			catch { }
#if UNITY_2019_1_OR_NEWER
			try
			{
				var vfx = prefab.GetComponentsInChildren<VisualEffect>(true);
				if ((vfx != null && vfx.Length > 0) && maxSeconds <= 0f)
				{
					maxSeconds = 2.0f;
				}
			}
			catch { }
#endif
			if (maxSeconds <= 0f) maxSeconds = 2.0f;
			maxSeconds += 0.2f;
			return Mathf.RoundToInt(maxSeconds * 1000f);
		}

		private static float GetCurveMax(ParticleSystem.MinMaxCurve curve)
		{
			switch (curve.mode)
			{
				case ParticleSystemCurveMode.Constant:
					return curve.constant;
				case ParticleSystemCurveMode.TwoConstants:
					return curve.constantMax;
				case ParticleSystemCurveMode.Curve:
					return MaxOfCurve(curve.curve);
				case ParticleSystemCurveMode.TwoCurves:
					return Mathf.Max(MaxOfCurve(curve.curve), MaxOfCurve(curve.curveMax));
				default:
					return 0f;
			}
		}

		private static float MaxOfCurve(AnimationCurve c)
		{
			if (c == null || c.length == 0) return 0f;
			float max = float.MinValue;
			for (int i = 0; i < c.length; i++)
			{
				max = Mathf.Max(max, c.keys[i].value);
			}
			return max <= 0f ? 0f : max;
		}

#if UNITY_EDITOR
		private static bool editorUpdateHooked;
		private static readonly List<(GameObject go, double end)> editorDelayedDestroy = new List<(GameObject, double)>();
		private struct ScheduledSpawn { public Transform source; public Vector3 target; public GameObject prefab; public double spawnTime; public int travelMs; public float arcDeg; }
		private static readonly List<ScheduledSpawn> editorSpawns = new List<ScheduledSpawn>();
		private struct MovingProjectile { public GameObject go; public Vector3 start; public Vector3 target; public double startTime; public double endTime; public float arcDeg; }
		private static readonly List<MovingProjectile> editorProjectiles = new List<MovingProjectile>();
		private static readonly List<GameObject> editorParticleRoots = new List<GameObject>();
		private static double lastEditorUpdateTime;

		public GameObject PlayAtEditor(Transform parent, GameObject prefab, int lifetimeMs, bool lookAtCamera = false)
		{
			var go = PlayAt(parent, prefab, lookAtCamera);
			if (go != null && lifetimeMs > 0)
			{
				ScheduleEditorDestroy(go, lifetimeMs);
			}
			RegisterEditorParticles(go);
			return go;
		}

		public void PlayProjectileEditor(Transform source, Vector3 targetPos, GameObject projectilePrefab, int travelMs, int launchDelayMs, float arcDegrees)
		{
			if (source == null || projectilePrefab == null || travelMs <= 0) return;
			HookEditorUpdate();
			double now = UnityEditor.EditorApplication.timeSinceStartup;
			editorSpawns.Add(new ScheduledSpawn { source = source, target = targetPos, prefab = projectilePrefab, spawnTime = now + Mathf.Max(0, launchDelayMs) / 1000.0, travelMs = travelMs, arcDeg = Mathf.Max(0f, arcDegrees) });
		}

		public void ScheduleEditorDestroy(GameObject go, int ms)
		{
			if (go == null || ms <= 0) return;
			HookEditorUpdate();
			double t = UnityEditor.EditorApplication.timeSinceStartup + (ms / 1000.0);
			editorDelayedDestroy.Add((go, t));
		}

		private static void HookEditorUpdate()
		{
			if (editorUpdateHooked) return;
			UnityEditor.EditorApplication.update += EditorUpdate;
			editorUpdateHooked = true;
			lastEditorUpdateTime = UnityEditor.EditorApplication.timeSinceStartup;
		}

		private static void EditorUpdate()
		{
			double now = UnityEditor.EditorApplication.timeSinceStartup;
			float dt = (float)System.Math.Max(0.0, now - lastEditorUpdateTime);
			lastEditorUpdateTime = now;

			// Spawns
			for (int i = editorSpawns.Count - 1; i >= 0; i--)
			{
				var s = editorSpawns[i];
				if (now >= s.spawnTime)
				{
					var src = s.source;
					if (src != null)
					{
						var dir = (s.target - src.position).normalized;
						var rot = dir == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(dir);
						var go = Instantiate(s.prefab, src.position, rot);
						Instance?.AutoPlay(go);
						RegisterEditorParticles(go);
						double dur = Mathf.Max(MinProjectileTravelSeconds, s.travelMs / 1000.0f);
						editorProjectiles.Add(new MovingProjectile { go = go, start = go.transform.position, target = s.target, startTime = now, endTime = now + dur, arcDeg = s.arcDeg });
					}
					editorSpawns.RemoveAt(i);
				}
			}

			// Move projectiles
			for (int i = editorProjectiles.Count - 1; i >= 0; i--)
			{
				var m = editorProjectiles[i];
				if (m.go == null)
				{
					editorProjectiles.RemoveAt(i);
					continue;
				}
				float t = (float)Mathf.InverseLerp((float)m.startTime, (float)m.endTime, (float)now);
				Vector3 basePos = Vector3.Lerp(m.start, m.target, Mathf.Clamp01(t));
				float arcRad = Mathf.Deg2Rad * Mathf.Max(0f, m.arcDeg);
				float half = 0.5f * Vector3.Distance(m.start, m.target);
				float apex = Mathf.Tan(arcRad) * half;
				float h = apex * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
				Vector3 pos = basePos + (Vector3.up * h);
				m.go.transform.position = pos;

				// Face direction of motion along the arc
				float nextT = (float)Mathf.InverseLerp((float)m.startTime, (float)m.endTime, (float)(now + Mathf.Max(0.001f, dt)));
				Vector3 nextBase = Vector3.Lerp(m.start, m.target, Mathf.Clamp01(nextT));
				float nextH = apex * Mathf.Sin(Mathf.PI * Mathf.Clamp01(nextT));
				Vector3 nextPos = nextBase + (Vector3.up * nextH);
				Vector3 vel = nextPos - pos;
				if (vel.sqrMagnitude > 0.0000001f)
				{
					m.go.transform.rotation = Quaternion.LookRotation(vel.normalized);
				}
				if (now >= m.endTime)
				{
					DestroyImmediate(m.go);
					editorProjectiles.RemoveAt(i);
				}
			}

			// Delayed destroys
			for (int i = editorDelayedDestroy.Count - 1; i >= 0; i--)
			{
				var d = editorDelayedDestroy[i];
				if (now >= d.end)
				{
					if (d.go != null) DestroyImmediate(d.go);
					editorDelayedDestroy.RemoveAt(i);
				}
			}

			// Manually simulate particle systems so they animate without selection
			for (int i = editorParticleRoots.Count - 1; i >= 0; i--)
			{
				var root = editorParticleRoots[i];
				if (root == null)
				{
					editorParticleRoots.RemoveAt(i);
					continue;
				}
				if (dt <= 0f) continue;
				var pss = root.GetComponentsInChildren<ParticleSystem>(true);
				for (int p = 0; p < pss.Length; p++)
				{
					var ps = pss[p];
					if (ps == null) continue;
					// Advance time without restarting, independent of selection
					ps.Simulate(dt, withChildren: false, restart: false, fixedTimeStep: false);
				}
			}

			// Ensure editor views repaint so animations are visible without user interaction
			UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
			UnityEditor.SceneView.RepaintAll();
		}

		private static void RegisterEditorParticles(GameObject go)
		{
			if (go == null) return;
			HookEditorUpdate();
			if (!editorParticleRoots.Contains(go)) editorParticleRoots.Add(go);
		}
#endif
	}
}


