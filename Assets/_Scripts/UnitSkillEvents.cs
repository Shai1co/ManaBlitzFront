using UnityEngine;
using UnityEngine.Events;

namespace ManaGambit
{
	public sealed class UnitSkillEvents : MonoBehaviour
	{
		public const int MaxSkillSlots = 4;
		private const int Skill0Index = 0;
		private const int Skill1Index = 1;
		private const int Skill2Index = 2;
		private const int Skill3Index = 3;

		[SerializeField] private UnityEvent onSkill0;
		[SerializeField] private UnityEvent onSkill1;
		[SerializeField] private UnityEvent onSkill2;
		[SerializeField] private UnityEvent onSkill3;

		[SerializeField] private UnityEvent onSkill0End;
		[SerializeField] private UnityEvent onSkill1End;
		[SerializeField] private UnityEvent onSkill2End;
		[SerializeField] private UnityEvent onSkill3End;

		public void InvokeForSkillIndex(int skillIndex)
		{
			int clamped = Mathf.Clamp(skillIndex, Skill0Index, MaxSkillSlots - 1);
			switch (clamped)
			{
				case Skill0Index:
					onSkill0?.Invoke();
					break;
				case Skill1Index:
					onSkill1?.Invoke();
					break;
				case Skill2Index:
					onSkill2?.Invoke();
					break;
				case Skill3Index:
					onSkill3?.Invoke();
					break;
			}
		}

		public void InvokeEndForSkillIndex(int skillIndex)
		{
			int clamped = Mathf.Clamp(skillIndex, Skill0Index, MaxSkillSlots - 1);
			switch (clamped)
			{
				case Skill0Index:
					onSkill0End?.Invoke();
					break;
				case Skill1Index:
					onSkill1End?.Invoke();
					break;
				case Skill2Index:
					onSkill2End?.Invoke();
					break;
				case Skill3Index:
					onSkill3End?.Invoke();
					break;
			}
		}
	}
}
