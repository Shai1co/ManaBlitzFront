using UnityEngine;

namespace ManaGambit.EditorTools
{
	/// <summary>
	/// Component that marks a GameObject as a drop parent target with customizable settings.
	/// When attached, this object will show a drop icon in the Scene view and accept drag-and-drop parenting.
	/// </summary>
	public class DropParentTarget : MonoBehaviour
	{
		[Header("Drop Parent Settings")]
		[Tooltip("The tag that this object must have to show the drop icon")]
		[SerializeField] private string requiredTag = "DropParent";
		
		[Header("Default Child Transform")]
		[Tooltip("Default local position for children when dropped onto this target")]
		[SerializeField] private Vector3 defaultLocalPosition = Vector3.zero;
		
		[Tooltip("Default local rotation for children when dropped onto this target")]
		[SerializeField] private Vector3 defaultLocalRotation = Vector3.zero;
		
		[Header("Icon Settings")]
		[Tooltip("Icon sprite to display in Scene view")]
		[SerializeField] private Sprite iconSprite;
		
		[Tooltip("Size of the icon in pixels")]
		[SerializeField] private float iconSize = 32f;
		
		[Tooltip("Offset from the object's position where the icon should appear")]
		[SerializeField] private Vector3 iconOffset = Vector3.zero;
		
		[Tooltip("Whether this drop parent target is enabled")]
		[SerializeField] private bool isEnabled = true;

		/// <summary>
		/// Gets the required tag for this drop parent target
		/// </summary>
		public string RequiredTag => requiredTag;
		
		/// <summary>
		/// Gets the default local position for children
		/// </summary>
		public Vector3 DefaultLocalPosition => defaultLocalPosition;
		
		/// <summary>
		/// Gets the default local rotation for children (as euler angles)
		/// </summary>
		public Vector3 DefaultLocalRotation => defaultLocalRotation;
		
		/// <summary>
		/// Gets the default local rotation for children (as quaternion)
		/// </summary>
		public Quaternion DefaultLocalRotationQuaternion => Quaternion.Euler(defaultLocalRotation);
		
		/// <summary>
		/// Gets the icon sprite for this target
		/// </summary>
		public Sprite IconSprite => iconSprite;
		
		/// <summary>
		/// Gets the icon size for this target
		/// </summary>
		public float IconSize => iconSize;
		
		/// <summary>
		/// Gets the icon offset for this target
		/// </summary>
		public Vector3 IconOffset => iconOffset;
		
		/// <summary>
		/// Gets whether this drop parent target is enabled
		/// </summary>
		public bool IsEnabled => isEnabled;

		/// <summary>
		/// Checks if this target is valid for drop parenting
		/// </summary>
		/// <returns>True if the target is enabled and has the required tag</returns>
		public bool IsValidTarget()
		{
			return isEnabled && !string.IsNullOrEmpty(requiredTag) && gameObject.CompareTag(requiredTag);
		}

		/// <summary>
		/// Applies the default transform settings to a child transform
		/// </summary>
		/// <param name="childTransform">The transform to apply settings to</param>
		public void ApplyDefaultTransform(Transform childTransform)
		{
			if (childTransform == null) return;
			
			childTransform.localPosition = defaultLocalPosition;
			childTransform.localRotation = DefaultLocalRotationQuaternion;
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			// Ensure icon size is reasonable
			iconSize = Mathf.Max(16f, iconSize);
			
			// Ensure tag is not null
			if (string.IsNullOrEmpty(requiredTag))
			{
				requiredTag = "DropParent";
			}
		}
#endif
	}
}
