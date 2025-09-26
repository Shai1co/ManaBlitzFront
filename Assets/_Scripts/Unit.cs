
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using HighlightPlus;

namespace ManaGambit
{

    public enum UnitState
    {
        Idle,
        Moving,
        WindUp,
        Attacking
    }

    public class Unit : MonoBehaviour
    {
        [SerializeField] private Vector2Int currentPosition;

        [SerializeField] private int currentHp;
        [SerializeField] private int maxHp;
        [SerializeField] private float currentMana;

        [SerializeField] private string unitID = "";
        [SerializeField] private string pieceId = "";
        [SerializeField] private string ownerId = "";

        // Cached HighlightEffect component to avoid repeated GetComponent calls
        private HighlightEffect cachedHighlightEffect;

        private CancellationTokenSource moveCts;
        private CancellationTokenSource _actionCts;
        private CancellationTokenSource _deathCts;
        private const float DefaultMoveDurationSeconds = 1f;
        private const float DeathAnimationDelaySeconds = 5f;
        private const float InstantMoveThresholdSeconds = 0.0001f;
        private const int DefaultBasicActionIndex = 0;
        private const float MinPlanarDirectionSqrMagnitude = 0.0001f;
        private const float HitFxInitialIntensity = 0.5f; // named constant instead of magic number
        private const float HitFxFadeOutSeconds = 0.2f; // named constant for fade duration

        // Events to coordinate UI/highlights with gameplay actions
        public event Action<Unit> OnMoveStarted;
        public event Action<Unit> OnMoveCompleted;
        public event Action<Unit> OnSkillStarted;
        public event Action<Unit> OnSkillCompleted;

        // Static event for unit death notification - allows ClickInput to clear highlights when selected unit dies
        public static event Action<Unit> OnUnitDied;

        // Static event for unit position changes - allows ClickInput to refresh highlights when selected unit moves via server
        public static event Action<Unit> OnUnitPositionChanged;

        /// <summary>
        /// Public method to trigger unit death event - used by external systems like NetworkManager during board resets
        /// </summary>
        public static void TriggerUnitDeathEvent(Unit unit)
        {
            if (unit != null)
            {
                try
                {
                    OnUnitDied?.Invoke(unit);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error triggering unit death event for {unit.name}: {e.Message}");
                }
            }
        }

        public Vector2Int CurrentPosition => currentPosition;

        public string UnitID => unitID;
        public string PieceId => pieceId;
        public string OwnerId => ownerId;

        public void SetUnitId(string id)
        {
            unitID = id;
        }

        public void SetPieceId(string id)
        {
            pieceId = id;
        }

        public void SetOwnerId(string id)
        {
            ownerId = id;
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

        public int CurrentHp => currentHp;
        public int MaxHp => maxHp;

        private ManaGambit.UnitAnimator unitAnimator;

        private UnitState currentState = UnitState.Idle;
        private bool isDead = false;

        // Damage numbers (DamageNumbersPro)
        [SerializeField] private DamageNumbersPro.DamageNumber damageNumberPrefab;
        [Min(0f)][SerializeField] private float damageNumberHeightOffset = 1.5f;

        private void Awake()
        {
            unitAnimator = GetComponent<ManaGambit.UnitAnimator>();
            if (unitAnimator == null)
            {
                Debug.LogWarning($"{name} missing UnitAnimator component");
            }

            // Cache HighlightEffect component
            cachedHighlightEffect = GetComponent<HighlightEffect>();
            if (cachedHighlightEffect == null)
            {
                cachedHighlightEffect = GetComponentInChildren<HighlightEffect>(true);
            }
        }

        public async UniTask Move(int x, int y)
        {
            await MoveTo(new Vector2Int(x, y));
        }

        public async UniTask MoveTo(Vector2Int target, string moveType = "Normal")
        {
            await MoveTo(target, DefaultMoveDurationSeconds, 0f, moveType);
        }

        public async UniTask MoveTo(Vector2Int target, float durationSeconds, float initialProgress, string moveType = "Normal")
        {
            Debug.Log($"💫 {name} MoveTo {target} duration={durationSeconds} initialProgress={initialProgress} moveType={moveType}");
            Debug.Log($"🎯 Movement: {transform.position} → {Board.Instance.GetSlotWorldPosition(target)}");
            if (moveCts != null)
            {
                moveCts.Cancel();
                moveCts.Dispose();
                moveCts = null;
            }

            moveCts = new CancellationTokenSource();
            var moveToken = moveCts.Token;

            Vector3 startPos = transform.position;
            Vector3 endPos = Board.Instance.GetSlotWorldPosition(target);
            float total = Mathf.Max(0f, durationSeconds);
            float elapsed = Mathf.Clamp01(initialProgress) * total;

            try
            {
                if (total <= InstantMoveThresholdSeconds || initialProgress >= 1f)
                {
                    OnMoveStarted?.Invoke(this);
                    // Face movement direction using local Y rotation only
                    Vector3 instantPlanarDir = new Vector3(endPos.x - startPos.x, 0f, endPos.z - startPos.z);
                    if (instantPlanarDir.sqrMagnitude > MinPlanarDirectionSqrMagnitude)
                    {
                        float instantYawDeg = Mathf.Atan2(instantPlanarDir.x, instantPlanarDir.z) * Mathf.Rad2Deg;
                        var e = transform.localEulerAngles;
                        e.y = instantYawDeg;
                        transform.localEulerAngles = e;
                    }
                    transform.position = endPos;
                    currentPosition = target;
                    // CRITICAL FIX: Don't force Idle for Teleport moves - server controls animation state
                    if (moveType != "Teleport" && moveType != "PostImpact")
                    {
                        SetState(UnitState.Idle);
                    }
                    OnMoveCompleted?.Invoke(this);
                    return;
                }

                // NOTE: Animation state (Moving/Idle) should be set by server data, not locally
                OnMoveStarted?.Invoke(this);

                // Face movement direction at movement start using local Y rotation only
                Vector3 planarDir = new Vector3(endPos.x - startPos.x, 0f, endPos.z - startPos.z);
                if (planarDir.sqrMagnitude > MinPlanarDirectionSqrMagnitude)
                {
                    float yawDeg = Mathf.Atan2(planarDir.x, planarDir.z) * Mathf.Rad2Deg;
                    var e = transform.localEulerAngles;
                    e.y = yawDeg;
                    transform.localEulerAngles = e;
                }

                while (elapsed < total)
                {
                    if (moveToken.IsCancellationRequested)
                    {
                        // CRITICAL FIX: Don't force Idle for special moves on cancellation - server controls animation state
                        if (moveType != "Teleport" && moveType != "PostImpact")
                        {
                            SetState(UnitState.Idle);
                        }
                        OnMoveCompleted?.Invoke(this);
                        return;
                    }

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / total);
                    transform.position = Vector3.Lerp(startPos, endPos, t);

                    try
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: moveToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // CRITICAL FIX: Don't force Idle for special moves on exception - server controls animation state
                        if (moveType != "Teleport" && moveType != "PostImpact")
                        {
                            SetState(UnitState.Idle);
                        }
                        OnMoveCompleted?.Invoke(this);
                        return;
                    }
                }

                transform.position = endPos;
                currentPosition = target;

                // CRITICAL FIX: Don't force Idle for special moves - server controls animation state
                if (moveType != "Teleport" && moveType != "PostImpact")
                {
                    SetState(UnitState.Idle);
                }
                OnMoveCompleted?.Invoke(this);
            }
            finally
            {
                if (moveCts != null)
                {
                    moveCts.Dispose();
                    moveCts = null;
                }
            }
        }

        public void Attack(int x, int y)
        {
            Attack(new Vector2Int(x, y));
        }

        public void Attack(Vector2Int target)
        {
            OnSkillStarted?.Invoke(this);
            transform.LookAt(Board.Instance.GetSlotWorldPosition(target));

            // Determine wind-up from config for default/basic action (index 0)
            int windUpMs = 0;
            if (NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
            {
                windUpMs = NetworkManager.Instance.UnitConfigAsset.GetWindUpMs(pieceId, DefaultBasicActionIndex);
            }
            Debug.Log($"{name} attacking position {target} (windUpMs={windUpMs})");

            // Mirror editor: do not play a dedicated WindUp animation. Begin the skill animation immediately.

            // Ensure animator plays the basic attack animation
            try { unitAnimator?.SetSkillIndex(DefaultBasicActionIndex); } catch { }
            SetState(UnitState.Attacking);

            // Schedule additional animation plays for multi-shot skills based on UnitConfig amount/interval
            try
            {
                var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
                var data = cfg != null ? cfg.GetData(pieceId) : null;
                ManaGambit.UnitConfig.ActionInfo action = null;
                if (data != null && data.actions != null && DefaultBasicActionIndex >= 0 && DefaultBasicActionIndex < data.actions.Length)
                {
                    action = data.actions[DefaultBasicActionIndex];
                }
                int amount = 1;
                int intervalMs = 0;
                if (action != null && action.attack != null)
                {
                    amount = Mathf.Max(1, action.attack.amount);
                    intervalMs = Mathf.Max(0, action.attack.interval);
                }
                if (amount > 1 && unitAnimator != null)
                {
                    // Use the same cancellation source as the action so repeats stop if the action is cancelled
                    if (_actionCts == null)
                    {
                        _actionCts = new CancellationTokenSource();
                    }
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _actionCts.Token);
                    for (int s = 1; s < amount; s++)
                    {
                        int delay = s * intervalMs;
                        UniTask.Void(async () =>
                        {
                            try { await UniTask.Delay(delay, cancellationToken: linked.Token); } catch { return; }
                            try { unitAnimator.PlaySkillShotNow(DefaultBasicActionIndex); } catch { }
                        });
                    }
                }
            }
            catch { }

            // TODO: trigger damage application here once server/client impact exists

            // Do not force Idle here; UnitAnimator auto-returns to Idle after the clip.
            OnSkillCompleted?.Invoke(this);
        }

        public async UniTask PlayUseSkill(UseSkillData use)
        {
            if (use == null) return;
            OnSkillStarted?.Invoke(this);

            // Determine target and handle movement-based skills
            var targetCell = use.target != null && use.target.cell != null ? new Vector2Int(use.target.cell.x, use.target.cell.y) : currentPosition;
            var targetWorldPos = Board.Instance.GetSlotWorldPosition(targetCell);

            // Check if this is a movement-based skill (like leap attack)
            bool isMovementSkill = false;
            if (use.origin != null)
            {
                var origin = new Vector2Int(use.origin.x, use.origin.y);
                isMovementSkill = origin != targetCell && Vector2Int.Distance(origin, targetCell) > 1;

                if (isMovementSkill)
                {
                    Debug.Log($"[Unit] Movement-based skill: {unitID} from {origin} to {targetCell} - server will handle positioning");
                }
            }

            // Face the target (important for both regular and movement skills)
            transform.LookAt(targetWorldPos);

            int windUpMs = 0;
            if (use.startTick > 0 && use.endWindupTick > use.startTick)
            {
                int dt = use.endWindupTick - use.startTick;
                windUpMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
            }
            if (windUpMs <= 0)
            {
                if (NetworkManager.Instance != null && NetworkManager.Instance.UnitConfigAsset != null)
                {
                    windUpMs = NetworkManager.Instance.UnitConfigAsset.GetWindUpMs(pieceId, Mathf.Max(0, use.skillId));
                }
            }

            // Mirror editor: do not enter a dedicated WindUp animation state

            // Ensure animator plays the specific skill animation
            try { unitAnimator?.SetSkillIndex(Mathf.Max(0, use.skillId)); } catch { }
            SetState(UnitState.Attacking);

            // If server provided a specific hit tick, optionally wait until then before returning to idle
            int hitDelayMs = 0;
            if (use.hitTick > 0 && use.endWindupTick > 0 && use.hitTick > use.endWindupTick)
            {
                int dt = use.hitTick - use.endWindupTick;
                hitDelayMs = Mathf.Max(0, Mathf.RoundToInt(dt * NetworkManager.DefaultTickRateMs));
            }
            if (hitDelayMs > 0)
            {
                try
                {
                    // Create new action cancellation token source if not already created
                    if (_actionCts == null)
                    {
                        _actionCts = new CancellationTokenSource();
                    }

                    // Create linked token for both destroy and manual cancellation
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _actionCts.Token);

                    await UniTask.Delay(hitDelayMs, cancellationToken: linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    SetState(UnitState.Idle);
                    OnSkillCompleted?.Invoke(this);
                    return;
                }
            }

            // Do not force Idle here; UnitAnimator auto-returns to Idle after the clip.
            OnSkillCompleted?.Invoke(this);
        }

        public void Stop()
        {
            if (moveCts != null)
            {
                moveCts.Cancel();
                moveCts.Dispose();
                moveCts = null;
            }

            if (_actionCts != null)
            {
                _actionCts.Cancel();
                _actionCts.Dispose();
                _actionCts = null;
            }

            if (_deathCts != null)
            {
                _deathCts.Cancel();
                _deathCts.Dispose();
                _deathCts = null;
            }

            Debug.Log($"{name} stopped");

            // Only set state to Idle if not dead
            if (!isDead)
            {
                SetState(UnitState.Idle);
            }
        }

        public void ApplyStatusChanges(StatusChange[] changes)
        {
            if (changes == null || changes.Length == 0) return;
            // Minimal placeholder: log and provide hook for future icon updates
            for (int i = 0; i < changes.Length; i++)
            {
                var c = changes[i];
                if (c == null) continue;
                Debug.Log($"[{name}] Status {c.name} {c.op} ({c.startTick}->{c.endTick})");
            }
            // Forward to a UnitStatusIcons component if present
            try
            {
                var behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b == null) continue;
                    var t = b.GetType();
                    if (t != null && t.Name == "UnitStatusIcons")
                    {
                        var mi = t.GetMethod("Apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null)
                        {
                            mi.Invoke(b, new object[] { changes });
                        }
                        break;
                    }
                }
            }
            catch { /* ignore icon errors */ }
        }

        public void ShowDamageNumber(int amount)
        {
            try
            {
                var pos = transform.position + Vector3.up * damageNumberHeightOffset;
                // Preferred: use assigned prefab field if present (doesn't need to be a child)
                var assignedField = damageNumberPrefab;
                if (assignedField != null)
                {
                    assignedField.Spawn(pos, (float)amount);
                }
                else
                {
                    // Fallback: find a DamageNumber in children
                    var prefab = GetComponentInChildren<DamageNumbersPro.DamageNumber>(true);
                    if (prefab != null)
                    {
                        prefab.Spawn(pos, (float)amount);
                    }
                }
            }
            catch { }
        }

        public void PlayHitFlash()
        {
            try
            {
                if (cachedHighlightEffect != null)
                {
                    cachedHighlightEffect.HitFX();
                }
            }
            catch { }
        }

        public void ShowEphemeralText(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            // Forward text-only display to UnitStatusIcons via reflection
            try
            {
                var behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b == null) continue;
                    var t = b.GetType();
                    if (t != null && t.Name == "UnitStatusIcons")
                    {
                        var mi = t.GetMethod("ShowTextKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null)
                        {
                            mi.Invoke(b, new object[] { key });
                        }
                        break;
                    }
                }
            }
            catch { /* ignore */ }
        }

        public void SetInitialData(UnitServerData data, bool setWorldPosition = false)
        {
            currentPosition = new Vector2Int(data.pos.x, data.pos.y);
            if (setWorldPosition)
            {
                transform.position = Board.Instance.GetSlotWorldPosition(currentPosition);
            }
            // Initialize HP and MaxHP with robust fallbacks mirroring JS client expectations
            maxHp = data.maxHp;
            if (maxHp <= 0)
            {
                // Fallback to config-defined hp for this pieceId
                var cfg = NetworkManager.Instance != null ? NetworkManager.Instance.UnitConfigAsset : null;
                var unitData = (cfg != null && !string.IsNullOrEmpty(pieceId)) ? cfg.GetData(pieceId) : null;
                if (unitData != null && unitData.hp > 0) maxHp = unitData.hp;
                if (maxHp <= 0) maxHp = Mathf.Max(1, data.hp); // last resort: use current hp or 1
            }
            currentHp = Mathf.Clamp(data.hp, 0, Mathf.Max(1, maxHp));
            ApplyHpUpdate(currentHp);
            currentMana = data.mana;

            // Apply server-provided animation state
            if (!string.IsNullOrEmpty(data.animState))
            {
                var serverAnimState = ParseAnimationState(data.animState);
                SetState(serverAnimState);
                Debug.Log($"{name} SetInitialData: Server provided animState '{data.animState}' -> {serverAnimState}");
            }
            // TODO: Update UI or other components with hp/mana
        }

        /// <summary>
        /// Apply server data update including position, HP, and animation state
        /// </summary>
        public void ApplyServerDataUpdate(UnitServerData data)
        {
            var currentPos = CurrentPosition;
            var newPos = new Vector2Int(data.pos.x, data.pos.y);

            // Handle position changes
            if (currentPos != newPos)
            {
                // Update position immediately (server is authoritative)
                currentPosition = newPos;
                transform.position = Board.Instance.GetSlotWorldPosition(newPos);
                Debug.Log($"{name} ApplyServerDataUpdate: Position changed from {currentPos} to {newPos}");

                // Notify systems that this unit's position changed (for highlight refresh)
                try
                {
                    OnUnitPositionChanged?.Invoke(this);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"{name} error notifying unit position change listeners: {e.Message}");
                }
            }

            // Apply HP update
            ApplyHpUpdate(data.hp);

            // Apply mana update
            currentMana = data.mana;

            // Apply server-provided animation state (most important for sync)
            if (!string.IsNullOrEmpty(data.animState))
            {
                var serverAnimState = ParseAnimationState(data.animState);
                SetState(serverAnimState);
                Debug.Log($"{name} ApplyServerDataUpdate: Server animState '{data.animState}' -> {serverAnimState}");
            }
        }

        public void ApplyHpUpdate(int newHp)
        {
            int previousHp = currentHp;
            int clamped = Mathf.Clamp(newHp, 0, Mathf.Max(1, maxHp));
            currentHp = clamped;
            try
            {
                var icons = GetComponent<UnitStatusIcons>();
                if (icons == null) icons = GetComponentInChildren<UnitStatusIcons>(true);
                if (icons != null)
                {
                    bool isOwn = AuthManager.Instance?.UserId != null &&
                                string.Equals(ownerId, AuthManager.Instance.UserId, StringComparison.Ordinal);
                    icons.SetHp(currentHp, Mathf.Max(1, maxHp), isOwn);
                }
            }
            catch { /* UI optional */ }

            // Trigger hit VFX when HP decreases
            if (previousHp > currentHp)
            {
                try
                {
                    if (cachedHighlightEffect != null)
                    {
                        // Use Highlight Plus HitFX with specified initial intensity
                        cachedHighlightEffect.HitFX(Color.white, HitFxFadeOutSeconds, HitFxInitialIntensity);
                    }
                }
                catch { }
            }
        }

        public void SetState(UnitState newState)
        {
            // Don't accept any state changes after unit is dead
            if (isDead)
            {
                Debug.Log($"{name} is dead, ignoring state change to {newState}");
                return;
            }

            // Allow re-triggering Attacking state even if already Attacking so consecutive casts replay
            if (!(newState == UnitState.Attacking && currentState == UnitState.Attacking))
            {
                if (currentState == newState) return;
                currentState = newState;
            }

            if (unitAnimator != null)
            {
                unitAnimator.SetState(newState);
            }

            // Additional state-specific logic if needed
        }

        public void Die()
        {
            if (isDead)
            {
                Debug.Log($"{name} is already dead, ignoring Die() call");
                return;
            }

            Debug.Log($"{name} is dying - playing death animation");
            isDead = true;

            // Notify any systems tracking this unit (e.g., ClickInput for highlight cleanup)
            try
            {
                OnUnitDied?.Invoke(this);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{name} error notifying unit death listeners: {e.Message}");
            }

            // Stop all ongoing actions
            Stop();

            // Disable colliders so unit can't be selected
            DisableColliders();

            // Play death animation
            if (unitAnimator != null)
            {
                unitAnimator.PlayDeath();
            }

            // Start delayed destruction
            StartDelayedDestruction().Forget();
        }

        private void DisableColliders()
        {
            try
            {
                // Disable all colliders on this GameObject
                var colliders = GetComponents<Collider>();
                foreach (var collider in colliders)
                {
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }
                }

                // Also disable colliders on children
                var childColliders = GetComponentsInChildren<Collider>();
                foreach (var collider in childColliders)
                {
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{name} failed to disable colliders: {e.Message}");
            }
        }

        private async UniTaskVoid StartDelayedDestruction()
        {
            try
            {
                _deathCts = new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    this.GetCancellationTokenOnDestroy(),
                    _deathCts.Token);
                var linkedToken = linkedCts.Token;

                int delayMs = Mathf.RoundToInt(DeathAnimationDelaySeconds * 1000f);
                await UniTask.Delay(delayMs, cancellationToken: linkedToken);

                // Clean up from board and game manager before destroying
                if (Board.Instance != null)
                {
                    Board.Instance.ClearOccupied(currentPosition, this);
                }
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.UnregisterUnit(unitID);
                }

                // CRITICAL FIX: Clean up skill tracking in NetworkManager to prevent memory leaks
                if (NetworkManager.Instance != null && !string.IsNullOrEmpty(unitID))
                {
                    NetworkManager.Instance.CleanupUnitSkillTracking(unitID);
                }

                Debug.Log($"{name} destruction timer completed - destroying GameObject");
                Destroy(gameObject);
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"{name} death animation cancelled");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{name} error during delayed destruction: {e.Message}");
            }
        }

        public bool IsDead => isDead;

        /// <summary>
        /// Get the cached HighlightEffect component
        /// </summary>
        public HighlightEffect GetHighlightEffect()
        {
            return cachedHighlightEffect;
        }

        /// <summary>
        /// Set selection highlight only if this unit is owned by the local player
        /// </summary>
        public void SetSelectionHighlight(bool highlighted)
        {
            // Only allow highlighting for player-owned units
            bool isOwn = AuthManager.Instance != null &&
                        string.Equals(ownerId, AuthManager.Instance.UserId, StringComparison.Ordinal);

            if (!isOwn && highlighted)
            {
                // Enemy units should never be highlighted for selection
                Debug.LogWarning($"[Unit] Attempted to highlight enemy unit {name} (owner: {ownerId}) - ignoring");
                return;
            }

            if (cachedHighlightEffect != null)
            {
                cachedHighlightEffect.SetHighlighted(highlighted);
            }
        }

        private void OnDestroy()
        {
            // If unit is being destroyed without going through Die(), still notify death event
            if (!isDead)
            {
                try
                {
                    OnUnitDied?.Invoke(this);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"{name} error notifying unit death listeners in OnDestroy: {e.Message}");
                }
            }

            // CRITICAL FIX: Clean up skill tracking in NetworkManager to prevent memory leaks
            if (NetworkManager.Instance != null && !string.IsNullOrEmpty(unitID))
            {
                NetworkManager.Instance.CleanupUnitSkillTracking(unitID);
            }

            // Ensure proper cleanup of cancellation token sources
            if (moveCts != null)
            {
                moveCts.Cancel();
                moveCts.Dispose();
                moveCts = null;
            }

            if (_actionCts != null)
            {
                _actionCts.Cancel();
                _actionCts.Dispose();
                _actionCts = null;
            }

            if (_deathCts != null)
            {
                _deathCts.Cancel();
                _deathCts.Dispose();
                _deathCts = null;
            }
        }
    }
}
