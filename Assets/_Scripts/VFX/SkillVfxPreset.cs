using UnityEngine;

namespace ManaGambit
{
	[CreateAssetMenu(fileName = "SkillVfxPreset", menuName = "ManaGambit/VFX/Skill VFX Preset", order = 10)]
	public class SkillVfxPreset : ScriptableObject
	{
		public enum WindupDurationPolicy
		{
			FromTicks,
			FixedMs
		}

		public enum SourceAnchor
		{
			Root,
			CastAnchor,
			ProjectileAnchor,
			Custom
		}

		public enum TargetAnchor
		{
			WorldAtTargetCell,
			TargetRoot,
			TargetImpactAnchor,
			Custom
		}

		[SerializeField, Tooltip("Action name this preset applies to (matches UnitConfig actions[].name)")] private string actionName = string.Empty;
		[SerializeField, Tooltip("Looping effect shown on the attacker while waiting for hit")] private GameObject windupPrefab;
		[SerializeField, Tooltip("Where to attach wind-up effect on the attacker")] private SourceAnchor windupAttach = SourceAnchor.CastAnchor;
		[SerializeField, Tooltip("How to determine wind-up duration")] private WindupDurationPolicy windupDurationPolicy = WindupDurationPolicy.FromTicks;
		[SerializeField, Tooltip("Used when WindupDurationPolicy is FixedMs")] private int fixedWindupMs = 500;

		[SerializeField, Tooltip("Projectile prefab (optional); if omitted, no travel phase")] private GameObject projectilePrefab;
		[SerializeField, Tooltip("Where to spawn projectile on the attacker")] private SourceAnchor projectileAttach = SourceAnchor.ProjectileAnchor;
		[SerializeField, Tooltip("If > 0, fixed projectile travel time in ms; if 0, use speed")] private int travelMs = 0;
		[SerializeField, Tooltip("Projectile speed in world units/second when travelMs == 0")] private float projectileSpeedUnitsPerSec = 0f;

		[SerializeField, Tooltip("Impact effect prefab (optional)")] private GameObject impactPrefab;
		[SerializeField, Tooltip("Where to place the impact effect")] private TargetAnchor impactAttach = TargetAnchor.WorldAtTargetCell;

		[SerializeField, Tooltip("Used when SourceAnchor.Custom is selected")] private string customSourceAnchorName;
		[SerializeField, Tooltip("Used when TargetAnchor.Custom is selected")] private string customTargetAnchorName;

		[SerializeField] private AudioClip audioCast;
		[SerializeField] private AudioClip audioImpact;

		public string ActionName => actionName;
		public GameObject WindupPrefab => windupPrefab;
		public SourceAnchor WindupAttach => windupAttach;
		public WindupDurationPolicy WindupPolicy => windupDurationPolicy;
		public int FixedWindupMs => fixedWindupMs;
		public GameObject ProjectilePrefab => projectilePrefab;
		public SourceAnchor ProjectileAttach => projectileAttach;
		public int TravelMs => travelMs;
		public float ProjectileSpeedUnitsPerSec => projectileSpeedUnitsPerSec;
		public GameObject ImpactPrefab => impactPrefab;
		public TargetAnchor ImpactAttach => impactAttach;
		public string CustomSourceAnchorName => customSourceAnchorName;
		public string CustomTargetAnchorName => customTargetAnchorName;
		public AudioClip AudioCast => audioCast;
		public AudioClip AudioImpact => audioImpact;
	}
}


