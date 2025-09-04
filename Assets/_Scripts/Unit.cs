
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

        private CancellationTokenSource moveCts;
        private CancellationTokenSource _actionCts;
        private const float DefaultMoveDurationSeconds = 1f;
        private const float InstantMoveThresholdSeconds = 0.0001f;
        private const int DefaultBasicActionIndex = 0;

        // Events to coordinate UI/highlights with gameplay actions
        public event Action<Unit> OnMoveStarted;
        public event Action<Unit> OnMoveCompleted;
        public event Action<Unit> OnSkillStarted;
        public event Action<Unit> OnSkillCompleted;

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

        public int CurrentHp => currentHp;
        public int MaxHp => maxHp;

        private ManaGambit.UnitAnimator unitAnimator;

        private UnitState currentState = UnitState.Idle;

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
        }

        public async UniTask Move(int x, int y)
        {
            await MoveTo(new Vector2Int(x, y));
        }

        public async UniTask MoveTo(Vector2Int target)
        {
            await MoveTo(target, DefaultMoveDurationSeconds, 0f);
        }

        public async UniTask MoveTo(Vector2Int target, float durationSeconds, float initialProgress)
        {
            Debug.Log($"{name} MoveTo {target} duration={durationSeconds} initialProgress={initialProgress}");

            if (moveCts != null)
            {
                moveCts.Cancel();
                moveCts.Dispose();
                moveCts = null;
            }

            moveCts = new CancellationTokenSource();

            Vector3 startPos = transform.position;
            Vector3 endPos = Board.Instance.GetSlotWorldPosition(target);
            float total = Mathf.Max(0f, durationSeconds);
            float elapsed = Mathf.Clamp01(initialProgress) * total;

            try
            {
                if (total <= InstantMoveThresholdSeconds || initialProgress >= 1f)
                {
                    OnMoveStarted?.Invoke(this);
                    transform.position = endPos;
                    currentPosition = target;
                    SetState(UnitState.Idle);
                    OnMoveCompleted?.Invoke(this);
                    return;
                }

                // Only enter Moving state when we will actually animate over time
                SetState(UnitState.Moving);
                OnMoveStarted?.Invoke(this);

                while (elapsed < total)
                {
                    if (moveCts.Token.IsCancellationRequested)
                    {
                        // Ensure lifecycle notification on cancellation and return to Idle
                        SetState(UnitState.Idle);
                        OnMoveCompleted?.Invoke(this);
                        return;
                    }

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / total);
                    transform.position = Vector3.Lerp(startPos, endPos, t);

                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: moveCts.Token);
                }

                transform.position = endPos;
                currentPosition = target;

                SetState(UnitState.Idle);
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

        public async void Attack(Vector2Int target)
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

            if (windUpMs > 0)
            {
                SetState(UnitState.WindUp);
                try
                {
                    // Create new action cancellation token source
                    _actionCts?.Cancel();
                    _actionCts?.Dispose();
                    _actionCts = new CancellationTokenSource();
                    
                    // Create linked token for both destroy and manual cancellation
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _actionCts.Token);
                    
                    // Wait for wind-up duration before the hit occurs
                    await UniTask.Delay(windUpMs, cancellationToken: linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    SetState(UnitState.Idle);
                    OnSkillCompleted?.Invoke(this);
                    return;
                }
            }

            SetState(UnitState.Attacking);

            // TODO: trigger damage application here once server/client impact exists

            SetState(UnitState.Idle);
            OnSkillCompleted?.Invoke(this);
        }

        public async UniTask PlayUseSkill(UseSkillData use)
        {
            if (use == null) return;
            OnSkillStarted?.Invoke(this);
            var targetCell = use.target != null && use.target.cell != null ? new Vector2Int(use.target.cell.x, use.target.cell.y) : currentPosition;
            transform.LookAt(Board.Instance.GetSlotWorldPosition(targetCell));

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

            if (windUpMs > 0)
            {
                SetState(UnitState.WindUp);
                try
                {
                    // Create new action cancellation token source
                    _actionCts?.Cancel();
                    _actionCts?.Dispose();
                    _actionCts = new CancellationTokenSource();
                    
                    // Create linked token for both destroy and manual cancellation
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _actionCts.Token);
                    
                    await UniTask.Delay(windUpMs, cancellationToken: linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    SetState(UnitState.Idle);
                    OnSkillCompleted?.Invoke(this);
                    return;
                }
            }

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
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _actionCts.Token);
                    
                    await UniTask.Delay(hitDelayMs, cancellationToken: linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    SetState(UnitState.Idle);
                    OnSkillCompleted?.Invoke(this);
                    return;
                }
            }

            SetState(UnitState.Idle);
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
            
            Debug.Log($"{name} stopped");
            SetState(UnitState.Idle);
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
				var hp = GetComponent<HighlightEffect>();
				if (hp != null)
				{
					hp.HitFX();
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
            // TODO: Update UI or other components with hp/mana
        }

        public void ApplyHpUpdate(int newHp)
        {
            int clamped = Mathf.Clamp(newHp, 0, Mathf.Max(1, maxHp));
            currentHp = clamped;
            try
            {
                var icons = GetComponent<UnitStatusIcons>();
                if (icons == null) icons = GetComponentInChildren<UnitStatusIcons>(true);
                if (icons != null)
                {
                    bool isOwn = AuthManager.Instance != null && 
                                !string.IsNullOrEmpty(AuthManager.Instance.UserId) && 
                                string.Equals(ownerId, AuthManager.Instance.UserId, StringComparison.Ordinal);
                    icons.SetHp(currentHp, Mathf.Max(1, maxHp), isOwn);
                }
            }
            catch { /* UI optional */ }
        }

        private void SetState(UnitState newState)
        {
            if (currentState == newState) return;

            currentState = newState;

            if (unitAnimator != null)
            {
                unitAnimator.SetState(newState);
            }

            // Additional state-specific logic if needed
        }

        private void OnDestroy()
        {
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
        }
    }
}
