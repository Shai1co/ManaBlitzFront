using UnityEngine;

namespace ManaGambit
{
	// Kept in a separate partial to avoid growing the main file beyond 500 lines
	public partial class UnitConfig : ScriptableObject
	{
		private const int NotFoundIndex = -1;

		/// <summary>
		/// Returns the action name for a given piece and action index, or null if unavailable.
		/// </summary>
		public string GetActionName(string pieceId, int actionIndex)
		{
			var data = GetData(pieceId);
			if (data == null || data.actions == null) return null;
			if (actionIndex < 0 || actionIndex >= data.actions.Length) return null;
			var action = data.actions[actionIndex];
			return action != null ? action.name : null;
		}

		/// <summary>
		/// Finds the action index for a given piece and action name, or -1 if not found.
		/// </summary>
		public int GetActionIndexByName(string pieceId, string actionName)
		{
			if (string.IsNullOrEmpty(actionName)) return NotFoundIndex;
			var data = GetData(pieceId);
			if (data == null || data.actions == null) return NotFoundIndex;
			for (int i = 0; i < data.actions.Length; i++)
			{
				var a = data.actions[i];
				if (a != null && string.Equals(a.name, actionName))
				{
					return i;
				}
			}
			return NotFoundIndex;
		}
	}
}


