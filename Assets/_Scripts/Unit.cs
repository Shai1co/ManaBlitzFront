
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace ManaGambit
{

public enum UnitState
{
    Idle,
    Moving,
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
        private const float MoveDuration = 1f;

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

        private ManaGambit.UnitAnimator unitAnimator;

        private UnitState currentState = UnitState.Idle;

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
            SetState(UnitState.Moving);

            if (moveCts != null)
            {
                moveCts.Cancel();
                moveCts.Dispose();
            }

            moveCts = new CancellationTokenSource();

            Vector3 startPos = transform.position;
            Vector3 endPos = Board.Instance.GetWorldPosition(target);
            float elapsed = 0f;

            try
            {
                while (elapsed < MoveDuration)
                {
                    if (moveCts.Token.IsCancellationRequested) return;

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / MoveDuration);
                    transform.position = Vector3.Lerp(startPos, endPos, t);

                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: moveCts.Token);
                }

                transform.position = endPos;
                currentPosition = target;

                SetState(UnitState.Idle);
            }
            finally
            {
                moveCts.Dispose();
                moveCts = null;
            }
        }

        public void Attack(int x, int y)
        {
            Attack(new Vector2Int(x, y));
        }

        public void Attack(Vector2Int target)
        {
            SetState(UnitState.Attacking);

            Debug.Log($"{name} attacking position {target}");
            transform.LookAt(Board.Instance.GetWorldPosition(target));

            // TODO: Add attack duration or wait for animation
            // For now, immediately back to idle; expand later
            SetState(UnitState.Idle);
        }

        public void Stop()
        {
            if (moveCts != null)
            {
                moveCts.Cancel();
            }
            // TODO: Stop other actions if any
            Debug.Log($"{name} stopped");
            SetState(UnitState.Idle);
        }

        public void SetInitialData(UnitServerData data, bool setWorldPosition = false)
        {
            currentPosition = new Vector2Int(data.pos.x, data.pos.y);
            if (setWorldPosition)
            {
                transform.position = Board.Instance.GetWorldPosition(currentPosition);
            }
            currentHp = data.hp;
            maxHp = data.maxHp;
            currentMana = data.mana;
            // TODO: Update UI or other components with hp/mana
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
    }
}
