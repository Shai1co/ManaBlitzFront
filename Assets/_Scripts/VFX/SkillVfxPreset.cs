using DarkTonic.MasterAudio;
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
		AttackerProjectileAnchor,
		AttackerRoot,
		Custom,
		// New: spawn at the closest point on the target's collider to the attacker's projectile anchor
		TargetColliderClosestPointToAttackerProjectileAnchor
	}

		[SerializeField, Tooltip("Action name this preset applies to (matches UnitConfig actions[].name)")] private string actionName = string.Empty;
		[SerializeField, Tooltip("Looping effect shown on the attacker while waiting for hit")] private GameObject windupPrefab;
		[SerializeField] private bool windupLookAtCamera;
		[SerializeField, Tooltip("Where to attach wind-up effect on the attacker")] private SourceAnchor windupAttach = SourceAnchor.CastAnchor;
		[SerializeField, Tooltip("How to determine wind-up duration")] private WindupDurationPolicy windupDurationPolicy = WindupDurationPolicy.FromTicks;
		[SerializeField, Tooltip("Used when WindupDurationPolicy is FixedMs")] private int fixedWindupMs = 500;

		[SerializeField, Tooltip("Projectile prefab (optional); if omitted, no travel phase")] private GameObject projectilePrefab;
		[SerializeField] private bool projectileLookAtCamera;
		[SerializeField, Tooltip("Where to spawn projectile on the attacker")] private SourceAnchor projectileAttach = SourceAnchor.ProjectileAnchor;
		[SerializeField, Tooltip("Where the projectile should target on the victim. Defaults to TargetImpactAnchor")]
		private TargetAnchor projectileTarget = TargetAnchor.TargetImpactAnchor;
		[SerializeField, Tooltip("If > 0, fixed projectile travel time in ms; if 0, use speed")] private int travelMs = 0;
		[SerializeField, Tooltip("Projectile speed in world units/second when travelMs == 0")] private float projectileSpeedUnitsPerSec = 0f;
		[SerializeField, Tooltip("Projectile speed sourced from UnitConfig (world units/second)")] private float projectileSpeedFromConfigUnitsPerSec = 0f;
		[SerializeField, Tooltip("Vertical arc apex angle in degrees for projectile flight (0 = straight line)")] private float projectileArcDegrees = 0f;

		[SerializeField, Tooltip("Impact effect prefab (optional)")] private GameObject impactPrefab;
		[SerializeField] private bool impactLookAtCamera;
		[SerializeField, Tooltip("Where to place the impact effect")] private TargetAnchor impactAttach = TargetAnchor.WorldAtTargetCell;
		[SerializeField, Tooltip("Offset the unit's position (in world units) towards the target when moving as part of this skill. Normal moves are unaffected. 0 = center of tile.")]
		private float attackMoveOffsetWorldUnits = 0f;

		[SerializeField, Header("Aura (optional)"), Tooltip("Aura prefab to spawn on the attacker (e.g., ring). Duration comes from UnitConfig unless overridden in code.")]
		private GameObject auraPrefab;
		[SerializeField, Tooltip("Where to attach the aura prefab on the attacker.")]
		private SourceAnchor auraAttach = SourceAnchor.Root;
		[SerializeField, Tooltip("If true, rotate aura to face camera on spawn.")]
		private bool auraLookAtCamera = false;

		[SerializeField, Header("Buff (optional)"), Tooltip("Buff prefab to spawn on targets (e.g., glow). Duration comes from UnitConfig unless overridden in code.")]
		private GameObject buffPrefab;
		[SerializeField, Tooltip("Where to place the buff effect on targets.")]
		private TargetAnchor buffAttach = TargetAnchor.TargetRoot;
		[SerializeField, Tooltip("If true, rotate buff to face camera on spawn.")]
		private bool buffLookAtCamera = false;
		[SerializeField, Tooltip("If true, force looping playback for the buff while active.")]
		private bool buffForceLoopWhileActive = true;

		[SerializeField, Tooltip("Used when SourceAnchor.Custom is selected")] private string customSourceAnchorName;
		[SerializeField, Tooltip("Used when TargetAnchor.Custom is selected")] private string customTargetAnchorName;

		[SerializeField,SoundGroup] private string audioCast;
		[SerializeField,SoundGroup] private string audioImpact;

		public string ActionName => actionName;
		public GameObject WindupPrefab => windupPrefab;
		public bool WindupLookAtCamera => windupLookAtCamera;
		public SourceAnchor WindupAttach => windupAttach;
		public WindupDurationPolicy WindupPolicy => windupDurationPolicy;
		public int FixedWindupMs => fixedWindupMs;
		public GameObject ProjectilePrefab => projectilePrefab;
		public bool ProjectileLookAtCamera => projectileLookAtCamera;
		public SourceAnchor ProjectileAttach => projectileAttach;
		public TargetAnchor ProjectileTarget => projectileTarget;
		public int TravelMs => travelMs;
		public float ProjectileSpeedUnitsPerSec => projectileSpeedUnitsPerSec;
		public float ProjectileSpeedFromConfigUnitsPerSec => projectileSpeedFromConfigUnitsPerSec;
		public float ProjectileArcDegrees => projectileArcDegrees;
		public GameObject ImpactPrefab => impactPrefab;
		public bool ImpactLookAtCamera => impactLookAtCamera;
		public TargetAnchor ImpactAttach => impactAttach;
		public float AttackMoveOffsetWorldUnits => attackMoveOffsetWorldUnits;
		public GameObject AuraPrefab => auraPrefab;
		public SourceAnchor AuraAttach => auraAttach;
		public bool AuraLookAtCamera => auraLookAtCamera;
		public GameObject BuffPrefab => buffPrefab;
		public TargetAnchor BuffAttach => buffAttach;
		public bool BuffLookAtCamera => buffLookAtCamera;
		public bool BuffForceLoopWhileActive => buffForceLoopWhileActive;
		public string CustomSourceAnchorName => customSourceAnchorName;
		public string CustomTargetAnchorName => customTargetAnchorName;
		public string AudioCast => audioCast;
		public string AudioImpact => audioImpact;

		#if UNITY_EDITOR
		private void OnValidate()
		{
			const int MinAllowedMs = 0;
			const float MinAllowedSpeed = 0f;
            const float MinAllowedOffset = 0f;

			// Clamp to minimums
			fixedWindupMs = Mathf.Max(MinAllowedMs, fixedWindupMs);
			travelMs = Mathf.Max(MinAllowedMs, travelMs);
			projectileSpeedUnitsPerSec = Mathf.Max(MinAllowedSpeed, projectileSpeedUnitsPerSec);
			projectileSpeedFromConfigUnitsPerSec = Mathf.Max(MinAllowedSpeed, projectileSpeedFromConfigUnitsPerSec);
			attackMoveOffsetWorldUnits = Mathf.Max(MinAllowedOffset, attackMoveOffsetWorldUnits);
			projectileArcDegrees = Mathf.Clamp(projectileArcDegrees, 0f, 89f);

			// Warn if projectile may not move
			// if (projectilePrefab != null && travelMs == MinAllowedMs && projectileSpeedUnitsPerSec == MinAllowedSpeed)
			// {
			// 	Debug.LogWarning($"[{nameof(SkillVfxPreset)}] Projectile may not move; both {nameof(travelMs)} and {nameof(projectileSpeedUnitsPerSec)} are 0 on '{name}'", this);
			// }
		}

		public void EditorSetProjectileSpeedFromConfig(float unitsPerSecond)
		{
			projectileSpeedFromConfigUnitsPerSec = Mathf.Max(0f, unitsPerSecond);
		}
		#endif
	}
}


