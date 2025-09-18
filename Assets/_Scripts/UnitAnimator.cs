
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
    public enum AnimationControlMode
    {
        DirectStateControl,
        ParameterStateControl
    }

    public class UnitAnimator : MonoBehaviour
    {
        [Header("Animation Control")]
        [SerializeField] private AnimationControlMode controlMode = AnimationControlMode.DirectStateControl;
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoReturnToIdleAfterSkills = true;
        [SerializeField] private float autoReturnIdleCrossfadeSeconds = 0.15f;
        private const int AutoReturnPaddingMs = 50;
        [SerializeField] private bool restoreDefaultYawAfterMove = true;
        [Tooltip("Very short crossfade used when stopping Walk due to arrival or stall.")]
        [SerializeField] private float moveStopIdleCrossfadeSeconds = 0.06f;
        [Header("Server-Driven Animation Control")]
        [Tooltip("Animation states are now controlled entirely by server data via UnitServerData.animState")]
        [SerializeField] private bool serverControlledAnimation = true; // Always true - kept for inspector visibility

        private const string IdleStateName = "Idle";
        private const string WalkStateName = "Walk";
        private const string AttackStateName = "Attack";
        private const string SkillStatePrefix = "Skill_"; // Will try Skill_0..Skill_3
        private const int BaseLayerIndex = 0; // Avoid magic number for base layer
        private const float ImmediateTransitionDuration = 0f; // Instant blend to avoid visible slide before pose
        private const string HitStateName = "GetHit";

        // Animator Parameter Names (matching the screenshot)
        private const string IdleParamName = "Idle";
        private const string WalkParamName = "Walk";
        private const string AttackParamName = "Attack";
        private const string DeathParamName = "Death";
        private const string GetHitParamName = "GetHit";
        private const string ReturnHomeParamName = "Return Home";
        private const string Skill0ParamName = "Skill_0";
        private const string Skill1ParamName = "Skill_1";
        private const string Skill2ParamName = "Skill_2";
        private const string Skill3ParamName = "Skill_3";

        // Smooth rotation constants
        private const float DefaultYawRotationDurationSeconds = 0.25f; // Short but smooth rotation back to default

        private int _lastSampleFrame = -1;
        private int _currentSkillIndex = -1; // -1 means unknown
        private System.Threading.CancellationTokenSource _autoReturnCts;
        private System.Threading.CancellationTokenSource _rotationCts;
        private float _initialLocalYawDegrees;
        private Unit _unit;
        private UnitState _currentState = UnitState.Idle;
        // NOTE: Motion detection fields removed - animation state now server-controlled

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null)
                Debug.LogError("UnitAnimator: No animator found on " + gameObject.name);
            _unit = GetComponent<Unit>();
        }

        private void Start()
        {
            // Capture the initial local yaw as the default facing to restore after moves
            _initialLocalYawDegrees = transform.localEulerAngles.y;
            if (_unit != null && restoreDefaultYawAfterMove)
            {
                _unit.OnMoveCompleted += HandleUnitMoveCompleted;
                _unit.OnMoveStarted += HandleUnitMoveStarted;
            }
        }

        public void SetState(UnitState state)
        {
            // Store the previous state before changing _currentState
            var prevState = _currentState;
            
            if (animator == null)
            {
                Debug.LogWarning($"UnitAnimator: Animator missing on {gameObject.name}. Cannot set state {state}.");
                return;
            }

            // Route to appropriate implementation based on control mode
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                SetStateUsingParameters(state, prevState);
            }
            else
            {
                SetStateUsingDirectControl(state, prevState);
            }
            
            // Only assign the new state after transitions complete
            _currentState = state;
            Debug.Log($"{name} UnitAnimator SetState {prevState} -> {state} (Mode: {controlMode})");
        }

        private void SetStateUsingDirectControl(UnitState state, UnitState prevState)
        {
            switch (state)
            {
                case UnitState.Idle:
                    // If transitioning from Moving to Idle, use a short crossfade for smooth stop
                    if (prevState == UnitState.Moving)
                    {
                        CrossfadeToIdle(moveStopIdleCrossfadeSeconds);
                    }
                    else
                    {
                        TryPlayState(IdleStateName);
                    }
                    CancelAutoReturn();
                    break;
                case UnitState.Moving:
                    // For walk, avoid crossfade to prevent late visual start; play immediately from time 0
                    TryPlayStateImmediate(WalkStateName);
                    CancelAutoReturn();
                    Debug.Log($"{name} UnitAnimator: Walk animation triggered for Moving state (server-controlled)");
                    break;
                case UnitState.WindUp:
                    // WindUp is no longer used - fall back to Idle
                    TryPlayState(IdleStateName);
                    CancelAutoReturn();
                    break;
                case UnitState.Attacking:
                    // Deprecated generic Attack state: only play skill-specific clips; fallback to Idle if missing
                    if (!TryPlaySkillState())
                    {
                        TryPlayState(IdleStateName);
                    }
                    // In play mode, automatically return to Idle after current clip completes
                    if (Application.isPlaying && autoReturnToIdleAfterSkills)
                    {
                        StartAutoReturnToIdleAfterCurrentClip().Forget();
                    }
                    break;
            }
        }

        private void SetStateUsingParameters(UnitState state, UnitState prevState)
        {
            switch (state)
            {
                case UnitState.Idle:
                    SetIdleParametersActive();
                    CancelAutoReturn();
                    break;
                case UnitState.Moving:
                    SetWalkParametersActive();
                    CancelAutoReturn();
                    Debug.Log($"{name} UnitAnimator: Walk parameters activated for Moving state (server-controlled)");
                    break;
                case UnitState.WindUp:
                    // WindUp is no longer used - fall back to Idle
                    SetIdleParametersActive();
                    CancelAutoReturn();
                    break;
                case UnitState.Attacking:
                    // Trigger skill-specific parameter if available
                    if (!TriggerSkillParameter())
                    {
                        TriggerParameter(AttackParamName);
                    }
                    // In play mode, automatically return to Idle after current clip completes
                    if (Application.isPlaying && autoReturnToIdleAfterSkills)
                    {
                        StartAutoReturnToIdleAfterCurrentClip().Forget();
                    }
                    break;
            }
        }

        private void Update()
        {
            // NOTE: Motion detection disabled - animation state is now controlled by server data
            // All animation state changes should come through SetState() calls from server updates
            return;
            
            // OLD MOTION DETECTION CODE DISABLED:
            // Animation state is now entirely server-driven via UnitServerData.animState
            // The server sends both position updates and animation states simultaneously
            // This ensures perfect synchronization between movement and animation across all clients
        }

        // Parameter-based animation control methods
        private void SetIdleParametersActive()
        {
            if (!HasParameter(IdleParamName) || !HasParameter(WalkParamName)) return;
            
            SetBoolParameter(IdleParamName, true);
            SetBoolParameter(WalkParamName, false);
            // Note: Death parameter is not managed here as it's for a different use case
        }

        private void SetWalkParametersActive()
        {
            if (!HasParameter(IdleParamName) || !HasParameter(WalkParamName)) return;
            
            SetBoolParameter(IdleParamName, false);
            SetBoolParameter(WalkParamName, true);
        }

        private bool TriggerSkillParameter()
        {
            if (_currentSkillIndex < 0 || _currentSkillIndex > 3) return false;
            
            string paramName = _currentSkillIndex switch
            {
                0 => Skill0ParamName,
                1 => Skill1ParamName,
                2 => Skill2ParamName,
                3 => Skill3ParamName,
                _ => null
            };
            
            if (paramName != null && HasParameter(paramName))
            {
                TriggerParameter(paramName);
                return true;
            }
            return false;
        }

        private bool HasParameter(string paramName)
        {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
        }

        private void SetBoolParameter(string paramName, bool value)
        {
            if (animator == null || !HasParameter(paramName)) return;
            try
            {
                animator.SetBool(paramName, value);
                // Force immediate sampling in parameter mode to match direct mode behavior
                if (controlMode == AnimationControlMode.ParameterStateControl && _lastSampleFrame != Time.frameCount)
                {
                    animator.Update(0f);
                    _lastSampleFrame = Time.frameCount;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"UnitAnimator: Failed to set bool parameter '{paramName}' to {value}: {e.Message}");
            }
        }

        private bool GetBoolParameter(string paramName)
        {
            if (animator == null || !HasParameter(paramName)) return false;
            try
            {
                return animator.GetBool(paramName);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"UnitAnimator: Failed to get bool parameter '{paramName}': {e.Message}");
                return false;
            }
        }

        private void TriggerParameter(string paramName)
        {
            if (animator == null || !HasParameter(paramName)) return;
            try
            {
                animator.SetTrigger(paramName);
                // Force immediate sampling in parameter mode to match direct mode behavior
                if (controlMode == AnimationControlMode.ParameterStateControl && _lastSampleFrame != Time.frameCount)
                {
                    animator.Update(0f);
                    _lastSampleFrame = Time.frameCount;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"UnitAnimator: Failed to trigger parameter '{paramName}': {e.Message}");
            }
        }

        private bool TryPlayState(string stateName)
        {
            // Check for state existence using both simple name and full path hash
            int stateHash = Animator.StringToHash(stateName);
            int fullPathHash = Animator.StringToHash($"Base Layer.{stateName}");

            bool hasByName = animator.HasState(BaseLayerIndex, stateHash);
            bool hasByFullPath = animator.HasState(BaseLayerIndex, fullPathHash);

            if (hasByName || hasByFullPath)
            {
                // If already in this state, restart from time 0 so repeated casts replay
                var info = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
                bool alreadyInState = (info.shortNameHash == stateHash) || (info.fullPathHash == fullPathHash);
                if (alreadyInState)
                {
                    int hashToPlay = hasByName ? stateHash : fullPathHash;
                    animator.Play(hashToPlay, BaseLayerIndex, 0f);
                    if (_lastSampleFrame != Time.frameCount)
                    {
                        animator.Update(0f);
                        _lastSampleFrame = Time.frameCount;
                    }
                    return true;
                }
                // Choose the hash that actually exists - prefer name hash, fallback to full path hash
                int hashToUse = hasByName ? stateHash : fullPathHash;
                
                // Ensure immediate visual switch to prevent movement starting while still in Idle pose
                animator.CrossFadeInFixedTime(hashToUse, ImmediateTransitionDuration, BaseLayerIndex, 0f);
                // Force-sample this frame so pose updates before any same-frame movement, but only once per frame
                if (_lastSampleFrame != Time.frameCount)
                {
                    animator.Update(0f);
                    _lastSampleFrame = Time.frameCount;
                }
                return true;
            }

            Debug.LogWarning($"UnitAnimator: Animation state '{stateName}' not found on {gameObject.name}. Skipping play.");
            return false;
        }

        private bool TryPlayStateImmediate(string stateName)
        {
            int stateHash = Animator.StringToHash(stateName);
            int fullPathHash = Animator.StringToHash($"Base Layer.{stateName}");
            bool hasByName = animator.HasState(BaseLayerIndex, stateHash);
            bool hasByFullPath = animator.HasState(BaseLayerIndex, fullPathHash);
            if (!(hasByName || hasByFullPath))
            {
                Debug.LogWarning($"UnitAnimator: Animation state '{stateName}' not found on {gameObject.name}. Skipping play.");
                return false;
            }
            int hashToUse = hasByName ? stateHash : fullPathHash;
            animator.Play(hashToUse, BaseLayerIndex, 0f);
            if (_lastSampleFrame != Time.frameCount)
            {
                animator.Update(0f);
                _lastSampleFrame = Time.frameCount;
            }
            return true;
        }

        private bool TryPlaySkillState()
        {
            if (_currentSkillIndex < 0 || _currentSkillIndex > 3)
            {
                return false;
            }
            string candidate = SkillStatePrefix + _currentSkillIndex.ToString();
            return TryPlayState(candidate);
        }

        public bool PlayReturnHomeState(int skillIndex)
        {
            if (animator == null) return false;
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, trigger the Return Home parameter
                if (HasParameter(ReturnHomeParamName))
                {
                    TriggerParameter(ReturnHomeParamName);
                    return true;
                }
                // Fall back to Idle if no Return Home parameter
                SetIdleParametersActive();
                return true;
            }
            else
            {
                // Direct state control mode
                int clamped = Mathf.Clamp(skillIndex, 0, 3);
                string specific = $"{SkillStatePrefix}{clamped}_ReturnHome";
                // Prefer skill-specific return-home if present; otherwise fall back to a generic "ReturnHome" state
                if (TryPlayState(specific)) return true;
                if (TryPlayState("ReturnHome")) return true;
                // As a last resort, fall back to Idle only (generic Attack is deprecated)
                return TryPlayState(IdleStateName);
            }
        }

        public void CrossfadeToIdle(float transitionSeconds)
        {
            if (animator == null) return;
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, just set Idle parameters active
                // Note: Parameter-based state machines handle their own transitions
                SetIdleParametersActive();
            }
            else
            {
                // Direct state control mode - use crossfade
                float dur = Mathf.Max(0f, transitionSeconds);
                int stateHash = Animator.StringToHash(IdleStateName);
                int fullPathHash = Animator.StringToHash($"Base Layer.{IdleStateName}");
                bool hasByName = animator.HasState(BaseLayerIndex, stateHash);
                bool hasByFullPath = animator.HasState(BaseLayerIndex, fullPathHash);
                if (hasByName || hasByFullPath)
                {
                    int hashToUse = hasByName ? stateHash : fullPathHash;
                    animator.CrossFadeInFixedTime(hashToUse, dur, BaseLayerIndex, 0f);
                }
                else
                {
                    // Fallback to immediate Idle
                    TryPlayState(IdleStateName);
                }

                // Ensure we sample at least once this frame so the transition begins visually
                if (_lastSampleFrame != Time.frameCount)
                {
                    animator.Update(0f);
                    _lastSampleFrame = Time.frameCount;
                }
            }
        }

        public void SetSkillIndex(int index)
        {
            _currentSkillIndex = Mathf.Clamp(index, -1, 3);
        }

        public void ClearSkillIndex()
        {
            _currentSkillIndex = -1;
        }

        public int GetCurrentSkillIndex()
        {
            return _currentSkillIndex;
        }

        public void PlaySkillShotNow(int skillIndex)
        {
            if (animator == null) return;
            SetSkillIndex(skillIndex);
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, trigger the appropriate skill parameter
                if (!TriggerSkillParameter())
                {
                    TriggerParameter(AttackParamName);
                }
            }
            else
            {
                // Direct state control mode
                if (!TryPlaySkillState())
                {
                    TryPlayState(AttackStateName);
                }
            }
            
            if (Application.isPlaying && autoReturnToIdleAfterSkills)
            {
                StartAutoReturnToIdleAfterCurrentClip().Forget();
            }
        }

        public void PlayGetHit()
        {
            if (animator == null) return;
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, trigger the GetHit parameter
                if (HasParameter(GetHitParamName))
                {
                    TriggerParameter(GetHitParamName);
                }
                else
                {
                    return;
                }
            }
            else
            {
                // Direct state control mode
                if (!TryPlayStateImmediate(HitStateName))
                {
                    return;
                }
            }
            
            if (Application.isPlaying && autoReturnToIdleAfterSkills)
            {
                StartAutoReturnToIdleAfterCurrentClip().Forget();
            }
        }

        public void PlayDeath()
        {
            if (animator == null) return;
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, set the Death parameter
                if (HasParameter(DeathParamName))
                {
                    SetBoolParameter(DeathParamName, true);
                    // Also clear other movement parameters
                    SetBoolParameter(IdleParamName, false);
                    SetBoolParameter(WalkParamName, false);
                }
                else
                {
                    Debug.LogWarning($"UnitAnimator: Death parameter '{DeathParamName}' not found on {gameObject.name}.");
                    return;
                }
            }
            else
            {
                // Direct state control mode - try to play Death state
                if (!TryPlayStateImmediate("Death"))
                {
                    Debug.LogWarning($"UnitAnimator: Death state not found on {gameObject.name}.");
                    return;
                }
            }
            
            // Cancel any auto-return as death is typically a final state
            CancelAutoReturn();
        }

        public void ResetFromDeath()
        {
            if (animator == null) return;
            
            if (controlMode == AnimationControlMode.ParameterStateControl)
            {
                // In parameter mode, clear the Death parameter and return to Idle
                if (HasParameter(DeathParamName))
                {
                    SetBoolParameter(DeathParamName, false);
                    SetIdleParametersActive();
                }
                else
                {
                    SetIdleParametersActive(); // Fallback to Idle
                }
            }
            else
            {
                // Direct state control mode - return to Idle
                TryPlayState(IdleStateName);
            }
        }

        // Editor-only helper to advance animations when testing outside Play Mode
        public void EditorTick(float deltaSeconds)
        {
            if (animator == null) return;
            if (deltaSeconds <= 0f) return;
            animator.Update(deltaSeconds);
        }

        public float GetCurrentStateLengthSeconds()
        {
            if (animator == null) return 0f;
            var info = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
            return info.length;
        }

        public float GetRemainingTimeSecondsForCurrentStateOneShot()
        {
            if (animator == null) return 0f;
            var info = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
            float len = info.length;
            float normalized = info.normalizedTime;
            
            // In parameter mode, the state machine might have different transition timing
            // Add a safety check to ensure we have valid timing info
            if (len <= 0f)
            {
                Debug.LogWarning($"UnitAnimator: Invalid animation length ({len}s) detected on {gameObject.name}. Using fallback timing.");
                return 1f; // Fallback timing
            }
            
            float progress = Mathf.Min(normalized, 1f);
            return Mathf.Max(0f, len * (1f - progress));
        }

        private void CancelAutoReturn()
        {
            try { _autoReturnCts?.Cancel(); _autoReturnCts?.Dispose(); } catch { }
            _autoReturnCts = null;
        }

        private async UniTaskVoid StartAutoReturnToIdleAfterCurrentClip()
        {
            CancelAutoReturn();
            _autoReturnCts = new System.Threading.CancellationTokenSource();
            using var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy(), _autoReturnCts.Token);
            int delayMs;
            try
            {
                float remaining = GetRemainingTimeSecondsForCurrentStateOneShot();
                delayMs = Mathf.Max(1, Mathf.RoundToInt(remaining * 1000f) + AutoReturnPaddingMs);
            }
            catch { delayMs = 200; }
            try { await UniTask.Delay(delayMs, cancellationToken: linked.Token); }
            catch { }
            if (animator != null)
            {
                CrossfadeToIdle(autoReturnIdleCrossfadeSeconds);
                ClearSkillIndex();
            }
        }

        private void OnDisable()
        {
            CancelAutoReturn();
            
            // Cancel and cleanup rotation
            if (_rotationCts != null)
            {
                _rotationCts.Cancel();
                _rotationCts.Dispose();
                _rotationCts = null;
            }
            
            if (_unit != null && restoreDefaultYawAfterMove)
            {
                _unit.OnMoveCompleted -= HandleUnitMoveCompleted;
                _unit.OnMoveStarted -= HandleUnitMoveStarted;
            }
        }

        private async UniTaskVoid RotateToDefaultYawSmooth()
        {
            // Cancel any existing rotation
            if (_rotationCts != null)
            {
                _rotationCts.Cancel();
                _rotationCts.Dispose();
                _rotationCts = null;
            }

            _rotationCts = new System.Threading.CancellationTokenSource();
            using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy(), 
                _rotationCts.Token);
            var linkedToken = linkedCts.Token;

            try
            {
                var startEuler = transform.localEulerAngles;
                var targetEuler = startEuler;
                targetEuler.y = _initialLocalYawDegrees;

                // Check if rotation is needed (avoid rotating if already close to target)
                float angleDiff = Mathf.DeltaAngle(startEuler.y, _initialLocalYawDegrees);
                if (Mathf.Abs(angleDiff) < 1f) // Less than 1 degree difference
                {
                    return;
                }

                float elapsed = 0f;
                while (elapsed < DefaultYawRotationDurationSeconds)
                {
                    if (linkedToken.IsCancellationRequested)
                        return;

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / DefaultYawRotationDurationSeconds);
                    
                    // Use smooth easing for more natural rotation
                    t = t * t * (3f - 2f * t); // Smoothstep

                    var currentEuler = transform.localEulerAngles;
                    currentEuler.y = Mathf.LerpAngle(startEuler.y, _initialLocalYawDegrees, t);
                    transform.localEulerAngles = currentEuler;

                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: linkedToken);
                }

                // Ensure exact final rotation
                var finalEuler = transform.localEulerAngles;
                finalEuler.y = _initialLocalYawDegrees;
                transform.localEulerAngles = finalEuler;
            }
            catch (System.OperationCanceledException)
            {
                // Rotation was cancelled, which is fine
            }
            finally
            {
                if (_rotationCts != null)
                {
                    _rotationCts.Dispose();
                    _rotationCts = null;
                }
            }
        }

        private void HandleUnitMoveStarted(Unit u)
        {
            // Cancel any ongoing rotation back to default when a new move starts
            if (_rotationCts != null)
            {
                _rotationCts.Cancel();
                _rotationCts.Dispose();
                _rotationCts = null;
            }
        }

        private void HandleUnitMoveCompleted(Unit u)
        {
            // Start smooth rotation back to default facing
            RotateToDefaultYawSmooth().Forget();
            
            // NOTE: Animation state is now controlled by server data, not local move completion
        }
    }
}
