
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace ManaGambit
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private Dictionary<string, Unit> units = new Dictionary<string, Unit>();
        private bool _isReloading;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }
        public void RestartScene()
        {
            if (_isReloading)
            {
                return;
            }
            RestartSceneAsync().Forget();
        }
        public async UniTask RestartSceneAsync()
        {
            if (_isReloading)
            {
                return;
            }
            try
            {
                _isReloading = true;
                // Clear singleton state before reload to avoid stale data
                ClearUnits();
                
                var sceneName = SceneManager.GetActiveScene().name;
                
                // Start async scene load
                var asyncOp = SceneManager.LoadSceneAsync(sceneName);
                
                if (asyncOp == null)
                {
                    Debug.LogError($"[GameManager] Failed to initiate async scene load for '{sceneName}'");
                    _isReloading = false;
                    return;
                }
                
                // Optionally prevent scene activation until ready
                // asyncOp.allowSceneActivation = false;
                
                // Wait for scene to complete loading
                await asyncOp.ToUniTask();
                
                if (asyncOp.isDone)
                {
                    Debug.Log($"[GameManager] Scene '{sceneName}' reloaded successfully");
                    _isReloading = false;
                }
                else
                {
                    Debug.LogError($"[GameManager] Scene '{sceneName}' failed to load properly");
                    // Scene reload failed - could implement fallback behavior here
                    _isReloading = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameManager] Error during scene restart: {e.Message}\nStack trace: {e.StackTrace}");
                // Restore safe state - reinitialize critical components
                ClearUnits();
                _isReloading = false;
            }
        }
        public void RegisterUnit(Unit unit, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"Unit {unit.name} has empty ID, not registering.");
                return;
            }

            if (units.ContainsKey(id))
            {
                Debug.LogWarning($"Duplicate unit ID {id} for {unit.name}");
                return;
            }

            units[id] = unit;
        }

        public Unit GetUnitById(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            units.TryGetValue(unitId, out var unit);
            return unit;
        }

        public List<Unit> GetAllUnits()
        {
            return new List<Unit>(units.Values);
        }

        public void ClearUnits()
        {
            units.Clear();
        }

        public void UnregisterUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            units.Remove(unitId);
        }

        public async UniTask ExecuteCommand(string unitID, string command, int x = -1, int y = -1)
        {
            if (!units.TryGetValue(unitID, out var unit))
            {
                Debug.LogWarning($"No unit found with ID {unitID}");
                return;
            }

            switch (command.ToLower())
            {
                case "move":
                    if (x >= 0 && y >= 0)
                    {
                        await unit.Move(x, y);
                    }
                    else
                    {
                        Debug.LogWarning("Move command requires x and y coordinates");
                    }
                    break;

                case "attack":
                    if (x >= 0 && y >= 0)
                    {
                        unit.Attack(x, y);
                    }
                    else
                    {
                        Debug.LogWarning("Attack command requires x and y coordinates");
                    }
                    break;

                case "stop":
                    unit.Stop();
                    break;

                default:
                    Debug.LogWarning($"Unknown command: {command}");
                    break;
            }
        }
    }
}
