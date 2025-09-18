using System.Threading;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;
using UnityEngine;

namespace ManaGambit
{
	public class UnitVfxController : MonoBehaviour
	{
		private const float MinProjectileTravelSeconds = 0.001f;
		private const int MinMilliseconds = 0;
		private const float MinLookDirMagnitudeSqr = 0.0001f;

	private Unit unit;
	private UnitVfxAnchors anchors;

	private GameObject activeWindup;
	private CancellationTokenSource vfxCts;
	
	// Distance-based idle transition tracking
	private Vector3 lastPosition;
	private bool isMonitoringMovement = false;
	private const float IdleTransitionDelay = 0.15f; // Wait briefly to ensure unit has stopped
	private const float PositionTolerance = 0.02f; // Distance tolerance for "reached destination"
	private const float MovementCheckInterval = 0.05f; // Check every 50ms instead of every frame

		private void Awake()
		{
			unit = GetComponent<Unit>();
			anchors = GetComponent<UnitVfxAnchors>();
			
			// Subscribe to movement events
			if (unit != null)
			{
				unit.OnMoveStarted += OnMoveStarted;
				unit.OnMoveCompleted += OnMoveCompleted;
			}
		}
		
		private void OnDestroy()
		{
			// Unsubscribe from events to prevent memory leaks
			if (unit != null)
			{
				unit.OnMoveStarted -= OnMoveStarted;
				unit.OnMoveCompleted -= OnMoveCompleted;
			}
		}
		
		private void OnMoveStarted(Unit movingUnit)
		{
			// Start monitoring movement when unit begins moving
			StartMovementMonitoring();
		}
		
		private void OnMoveCompleted(Unit completedUnit)
		{
			// Immediately try to transition to idle when movement event fires
			TransitionToIdleAfterDelay().Forget();
		}
		
		private void StartMovementMonitoring()
		{
			if (!isMonitoringMovement)
			{
				isMonitoringMovement = true;
				lastPosition = transform.position;
				MonitorMovementCoroutine().Forget();
			}
		}
		
		private async UniTaskVoid MonitorMovementCoroutine()
		{
			try
			{
				while (isMonitoringMovement && unit != null && this != null)
				{
					await UniTask.Delay(System.TimeSpan.FromSeconds(MovementCheckInterval), cancellationToken: this.GetCancellationTokenOnDestroy());
					
					if (!isMonitoringMovement || unit == null) break;
					
					Vector3 currentPosition = transform.position;
					float distanceMoved = Vector3.Distance(currentPosition, lastPosition);
					
					if (distanceMoved <= PositionTolerance)
					{
						// Unit appears stationary - wait a bit longer then transition to idle
						await UniTask.Delay(System.TimeSpan.FromSeconds(IdleTransitionDelay), cancellationToken: this.GetCancellationTokenOnDestroy());
						
						// Double-check unit is still stationary after delay
						if (isMonitoringMovement && unit != null && Vector3.Distance(transform.position, currentPosition) <= PositionTolerance)
						{
							TransitionToIdle();
							isMonitoringMovement = false; // Stop monitoring until next movement
						}
					}
					else
					{
						// Unit is still moving, update position
						lastPosition = currentPosition;
					}
				}
			}
			catch (System.OperationCanceledException)
			{
				// Component destroyed - expected behavior
			}
			catch (System.Exception e)
			{
				Debug.LogWarning($"[UnitVfxController] Movement monitoring error: {e.Message}");
			}
			finally
			{
				isMonitoringMovement = false;
			}
		}
		
		private async UniTaskVoid TransitionToIdleAfterDelay()
		{
			try
			{
				// Wait briefly to ensure movement has fully stopped
				await UniTask.Delay(System.TimeSpan.FromSeconds(0.1f), cancellationToken: this.GetCancellationTokenOnDestroy());
				
				// Stop any ongoing movement monitoring since movement completed
				isMonitoringMovement = false;
				
				// Transition to idle
				TransitionToIdle();
			}
			catch (System.OperationCanceledException)
			{
				// Component destroyed - expected behavior  
			}
		}

		public void CancelAll()
		{
			try
			{
				vfxCts?.Cancel();
				vfxCts?.Dispose();
				vfxCts = null;
			}
			catch { }
			if (activeWindup != null)
			{
				Destroy(activeWindup);
				activeWindup = null;
			}
			
			// Stop movement monitoring when cancelling all VFX
			isMonitoringMovement = false;
		}

		public void StopWindupNow()
		{
			if (activeWindup != null)
			{
				Destroy(activeWindup);
				activeWindup = null;
			}
		}

		public async UniTaskVoid HandleUseSkillVfx(UseSkillData use, int serverTick, UnitConfig unitConfig)
		{
			if (use == null || VfxManager.Instance == null || unit == null) return;
			var preset = VfxManager.Instance.GetPresetByPieceAndIndex(unit.PieceId, Mathf.Max(0, use.skillId), unitConfig);
			if (preset == null) return;

		// NOTE: Movement handling removed - all movement is now server-authoritative
		// Server sends Move events for leap behaviors, client should only handle VFX
		// Leap positioning is calculated by server and sent via Move events

			int delayMs = 0;
			if (use.hitTick > 0 && serverTick > 0 && use.hitTick > serverTick)
			{
				int dt = use.hitTick - serverTick;
				delayMs = Mathf.Max(MinMilliseconds, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
			}

			int windupDurationMs = 0;
			if (preset.WindupPolicy == SkillVfxPreset.WindupDurationPolicy.FixedMs)
			{
				windupDurationMs = Mathf.Max(100, preset.FixedWindupMs); // Minimum 100ms for visibility
			}
			else
			{
				if (use.startTick > 0 && use.endWindupTick > use.startTick)
				{
					int dt = use.endWindupTick - use.startTick;
					windupDurationMs = Mathf.Max(100, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs)); // Minimum 100ms
				}
				else
				{
					// If no server timing provided, use a reasonable default based on delay
					windupDurationMs = Mathf.Max(200, delayMs / 2); // At least 200ms, or half the total delay
				}
			}

			Transform windupAnchor = ResolveSourceAnchor(preset.WindupAttach, preset.CustomSourceAnchorName);
			Transform projectileAnchor = ResolveSourceAnchor(preset.ProjectileAttach, preset.CustomSourceAnchorName);

			// Determine target world position early and remember it for impact
			Vector3 targetWorldPos = transform.position;
			UnityEngine.Vector2Int targetCell = new UnityEngine.Vector2Int(unit.CurrentPosition.x, unit.CurrentPosition.y);
			if (use.target != null && use.target.cell != null)
			{
				targetCell = new UnityEngine.Vector2Int(use.target.cell.x, use.target.cell.y);
				targetWorldPos = Board.Instance.GetSlotWorldPosition(targetCell);
			}
			VfxManager.Instance.RememberTargetPosition(unit.UnitID, targetWorldPos);

			// Prepare CTS
			vfxCts?.Cancel();
			vfxCts?.Dispose();
			vfxCts = new CancellationTokenSource();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), vfxCts.Token);

			// Wind-up
			if (preset.WindupPrefab != null && windupDurationMs > 0 && windupAnchor != null)
			{
				activeWindup = VfxManager.Instance.PlayAt(windupAnchor, preset.WindupPrefab);
				if (preset.AudioCast != string.Empty)
				{
					MasterAudio.PlaySound3DAtVector3AndForget(preset.AudioCast, windupAnchor.position);
				}
				try { await UniTask.Delay(windupDurationMs, cancellationToken: linked.Token); }
				catch { }
				StopWindupNow();
			}

			// Projectile - always spawn if preset has projectile (mirror JS client behavior)
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
					travelMs = Mathf.Max(50, Mathf.RoundToInt(seconds * 1000f)); // Minimum 50ms like JS client
				}
				else
				{
					// Default travel time for instant skills (like JS client default of 200ms)
					travelMs = 200;
				}
				
				// Always spawn projectile, but time it correctly
				if (delayMs > 0 && travelMs > 0)
				{
					// For skills with proper timing, launch projectile so it arrives at hitTick
					int launchDelayMs = Mathf.Max(0, delayMs - travelMs);
					await UniTask.Delay(launchDelayMs, cancellationToken: linked.Token).SuppressCancellationThrow();
					await VfxManager.Instance.PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, linked.Token);
				}
				else
				{
					// For instant skills or missing timing, spawn projectile immediately with default travel
					await VfxManager.Instance.PlayProjectile(projectileAnchor, targetWorldPos, preset.ProjectilePrefab, travelMs, preset.ProjectileLookAtCamera, preset.ProjectileArcDegrees, linked.Token);
				}
			}

		// NOTE: Post-impact movement handling removed - all movement is now server-authoritative
		// Server calculates ReturnHome and LandNearTarget behaviors and sends Move events
		// Client should only handle VFX, not position updates
		}

		private Transform ResolveSourceAnchor(SkillVfxPreset.SourceAnchor which, string customName)
		{
			if (anchors == null) return transform;
			return anchors.FindSourceAnchor(which, customName);
		}

		private Transform ResolveTargetAnchor(Unit target, SkillVfxPreset.TargetAnchor which, string customName)
		{
			if (target == null) return null;
			var ta = target.GetComponent<UnitVfxAnchors>();
			if (ta == null) return target.transform;
			return ta.FindTargetAnchor(which, customName);
		}

		// REMOVED: HandleMovement method - all movement is now server-authoritative
		// Server calculates leap destinations and sends Move events
		// Client should not perform local movement calculations

		private float EstimateTileWorldSize(Board board, Vector2Int coord)
		{
			if (board == null) return 1f;
			var right = new Vector2Int(Mathf.Min(Board.Size - 1, coord.x + 1), coord.y);
			var up = new Vector2Int(coord.x, Mathf.Min(Board.Size - 1, coord.y + 1));
			Vector3 c = board.GetSlotWorldPosition(coord);
			Vector3 r = board.GetSlotWorldPosition(right);
			Vector3 u = board.GetSlotWorldPosition(up);
			float sx = Mathf.Max(0.001f, Vector3.Distance(c, r));
			float sy = Mathf.Max(0.001f, Vector3.Distance(c, u));
			return Mathf.Max(sx, sy);
		}

		// REMOVED: HandlePostImpact method - all post-impact movement is now server-authoritative
		// Server handles ReturnHome and LandNearTarget behaviors and sends Move events

		private void TransitionToIdle(float crossfadeDuration = 0.15f)
		{
			if (unit == null) return;
			
			var unitAnimator = unit.GetComponent<UnitAnimator>();
			if (unitAnimator != null)
			{
				// Only transition if not already in an appropriate idle-like state
				// Let the animation system handle the actual state checking
				unitAnimator.CrossfadeToIdle(crossfadeDuration);
				unitAnimator.ClearSkillIndex();
			}
		}

		// REMOVED: FindLandNearTargetCell method - server calculates landing positions

		// REMOVED: InBounds and IsOccupied helper methods - no longer needed for server-authoritative movement
	}
}


