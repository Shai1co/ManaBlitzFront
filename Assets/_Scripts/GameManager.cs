
using System.Collections.Generic;
using UnityEngine;

namespace ManaGambit
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private Dictionary<string, Unit> units = new Dictionary<string, Unit>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
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

        public void ExecuteCommand(string unitID, string command, int x = -1, int y = -1)
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
                        unit.Move(x, y);
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
