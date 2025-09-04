using System.Collections.Generic;
using UnityEngine;

namespace ManaGambit
{
	[CreateAssetMenu(fileName = "SkillVfxDatabase", menuName = "ManaGambit/VFX/Skill VFX Database", order = 11)]
	public class SkillVfxDatabase : ScriptableObject
	{
		[Tooltip("List of Skill VFX presets. Ensure each preset is unique — no duplicate entries.")]
		[SerializeField]
		private List<SkillVfxPreset> presets = new List<SkillVfxPreset>();

		private Dictionary<string, SkillVfxPreset> cachedByName;

		public SkillVfxPreset GetByActionName(string actionName)
		{
			if (cachedByName == null)
			{
				cachedByName = new Dictionary<string, SkillVfxPreset>();
				for (int i = 0; i < presets.Count; i++)
				{
					var p = presets[i];
					if (p != null && !string.IsNullOrEmpty(p.ActionName)) cachedByName[p.ActionName] = p;
				}
			}
			return cachedByName != null && !string.IsNullOrEmpty(actionName) && cachedByName.TryGetValue(actionName, out var preset) ? preset : null;
		}
	}
}


