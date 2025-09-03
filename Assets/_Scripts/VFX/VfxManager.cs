using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ManaGambit
{
	public class VfxManager : MonoBehaviour
	{
		public static VfxManager Instance { get; private set; }

		[SerializeField] private SkillVfxDatabase database;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		public SkillVfxPreset GetPresetByActionName(string actionName)
		{
			return database != null ? database.GetByActionName(actionName) : null;
		}

		public SkillVfxPreset GetPresetByPieceAndIndex(string pieceId, int actionIndex, UnitConfig unitConfig)
		{
			if (unitConfig == null) return null;
			var actionName = unitConfig.GetActionName(pieceId, actionIndex);
			if (string.IsNullOrEmpty(actionName)) return null;
			return GetPresetByActionName(actionName);
		}

		public GameObject PlayAt(Transform parent, GameObject prefab)
		{
			if (prefab == null || parent == null) return null;
			var go = Instantiate(prefab, parent.position, parent.rotation, parent);
			return go;
		}

		public GameObject PlayAt(Vector3 position, Quaternion rotation, GameObject prefab)
		{
			if (prefab == null) return null;
			var go = Instantiate(prefab, position, rotation);
			return go;
		}

		public async UniTask PlayProjectile(Transform source, Vector3 targetPos, GameObject projectilePrefab, int travelMs, CancellationToken token)
		{
			if (projectilePrefab == null || source == null) return;
			var proj = Instantiate(projectilePrefab, source.position, Quaternion.LookRotation((targetPos - source.position).normalized));
			float travelSeconds = Mathf.Max(0.001f, travelMs / 1000f);
			Vector3 startPos = proj.transform.position;
			float elapsed = 0f;
			while (elapsed < travelSeconds)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, token);
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / travelSeconds);
				proj.transform.position = Vector3.Lerp(startPos, targetPos, t);
			}
			proj.transform.position = targetPos;
			Destroy(proj);
		}
	}
}


