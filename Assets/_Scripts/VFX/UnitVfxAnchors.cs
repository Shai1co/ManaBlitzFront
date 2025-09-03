using UnityEngine;

namespace ManaGambit
{
	public class UnitVfxAnchors : MonoBehaviour
	{
		[Header("Common Anchors")]
		public Transform castAnchor;
		public Transform projectileAnchor;
		public Transform impactAnchor;

		public Transform FindSourceAnchor(SkillVfxPreset.SourceAnchor anchor, string customName)
		{
			switch (anchor)
			{
				case SkillVfxPreset.SourceAnchor.CastAnchor: return castAnchor != null ? castAnchor : transform;
				case SkillVfxPreset.SourceAnchor.ProjectileAnchor: return projectileAnchor != null ? projectileAnchor : transform;
				case SkillVfxPreset.SourceAnchor.Custom:
					if (!string.IsNullOrEmpty(customName))
					{
						var t = transform.Find(customName);
						if (t != null) return t;
					}
					return transform;
				default:
					return transform;
			}
		}

		public Transform FindTargetAnchor(SkillVfxPreset.TargetAnchor anchor, string customName)
		{
			switch (anchor)
			{
				case SkillVfxPreset.TargetAnchor.TargetImpactAnchor: return impactAnchor != null ? impactAnchor : transform;
				case SkillVfxPreset.TargetAnchor.Custom:
					if (!string.IsNullOrEmpty(customName))
					{
						var t = transform.Find(customName);
						if (t != null) return t;
					}
					return transform;
				default:
					return transform;
			}
		}
	}
}


