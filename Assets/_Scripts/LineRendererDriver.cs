using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
	/// <summary>
	/// This component, added to a line renderer, will let you fill a list of transforms, and bind their positions to the linerenderer's positions.
	/// </summary>
	[ExecuteAlways]
	[RequireComponent(typeof(LineRenderer))]
	public class LineRendererDriver : MonoBehaviour
	{
		[Header("Position Drivers")]

		/// the list of targets - their quantity has to match the LineRenderer's positions count
		public List<Transform> Targets;
		/// whether or not to keep both in sync at update
		public bool BindPositionsToTargetsAtUpdate = true;

		[Header("Binding")]

		protected LineRenderer _lineRenderer;
		protected bool _countsMatch = false;

		/// <summary>
		/// On Awake we initialize our driver
		/// </summary>
		protected virtual void Awake()
		{
			Initialization();
		}

		/// <summary>
		/// Ensures initialization when the component is enabled, including in Edit Mode.
		/// </summary>
		protected virtual void OnEnable()
		{
			Initialization();
		}

		#if UNITY_EDITOR
		/// <summary>
		/// Keeps references in sync when values change in the inspector.
		/// </summary>
		protected virtual void OnValidate()
		{
			Initialization();
			if (!Application.isPlaying && BindPositionsToTargetsAtUpdate)
			{
				BindPositionsToTargets();
			}
		}
		#endif

		/// <summary>
		/// Grabs the line renderer, tests counts
		/// </summary>
		protected virtual void Initialization()
		{
			_lineRenderer = this.gameObject.GetComponent<LineRenderer>();
			_countsMatch = CheckPositionCounts();
			if (!_countsMatch)
			{
				Debug.LogWarning(this.name + ", LineRendererDriver's Targets list doesn't have the same amount of entries as the LineRender's Positions array. It won't work.");
			}
		}

		/// <summary>
		/// On Update we bind our positions to targets if needed
		/// </summary>
		protected virtual void Update()
		{
			if (BindPositionsToTargetsAtUpdate)
			{
				BindPositionsToTargets();
			}
		}

		/// <summary>
		/// A method meant to be called by the inspector button
		/// </summary>
		[Button("Bind")]
		protected virtual void Bind()
		{
			Initialization();
			BindPositionsToTargets();
		}

		/// <summary>
		/// Goes through all the targets and assigns their positions to the LineRenderer's positions
		/// </summary>
		public virtual void BindPositionsToTargets()
		{
			if (_lineRenderer == null || Targets == null)
			{
				return;
			}

			_countsMatch = CheckPositionCounts();
			if (!_countsMatch)
			{
				return;
			}

			for (int i = 0; i < Targets.Count; i++)
			{
				if (Targets[i] == null)
				{
					continue;
				}
				_lineRenderer.SetPosition(i, Targets[i].position);
			}
		}

		/// <summary>
		/// Makes sure the counts match
		/// </summary>
		/// <returns></returns>
		protected virtual bool CheckPositionCounts()
		{
			if (_lineRenderer == null || Targets == null)
			{
				return false;
			}
			return Targets.Count == _lineRenderer.positionCount;
		}
	}
