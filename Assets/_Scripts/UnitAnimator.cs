
using UnityEngine;

namespace ManaGambit
{
    public class UnitAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        private const string IdleStateName = "Idle";
        private const string WalkStateName = "Walk";
        private const string AttackStateName = "Attack";
        private const string WindUpStateName = "WindUp"; // optional; falls back if missing
        private const int BaseLayerIndex = 0; // Avoid magic number for base layer
        private const float ImmediateTransitionDuration = 0f; // Instant blend to avoid visible slide before pose

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null)
                Debug.LogError("UnitAnimator: No animator found on " + gameObject.name);
        }

        public void SetState(UnitState state)
        {
            if (animator == null)
            {
                Debug.LogWarning($"UnitAnimator: Animator missing on {gameObject.name}. Cannot set state {state}.");
                return;
            }

            switch (state)
            {
                case UnitState.Idle:
                    TryPlayState(IdleStateName);
                    break;
                case UnitState.Moving:
                    TryPlayState(WalkStateName);
                    break;
                case UnitState.WindUp:
                    if (!TryPlayState(WindUpStateName))
                    {
                        // If no dedicated wind-up state, optionally hold Idle/Walk
                        TryPlayState(IdleStateName);
                    }
                    break;
                case UnitState.Attacking:
                    TryPlayState(AttackStateName);
                    break;
            }
            Debug.Log($"{name} UnitAnimator SetState {state}");
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
                // Ensure immediate visual switch to prevent movement starting while still in Idle pose
                animator.CrossFadeInFixedTime(stateName, ImmediateTransitionDuration, BaseLayerIndex, 0f);
                // Force-sample this frame so pose updates before any same-frame movement
                animator.Update(0f);
                return true;
            }

            Debug.LogWarning($"UnitAnimator: Animation state '{stateName}' not found on {gameObject.name}. Skipping play.");
            return false;
        }
    }
}
