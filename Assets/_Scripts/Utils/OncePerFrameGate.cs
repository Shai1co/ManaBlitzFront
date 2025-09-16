using UnityEngine;

namespace ManaGambit
{
	/// <summary>
	/// Lightweight guard to ensure an action for a specific key only runs once per frame.
	/// </summary>
	public sealed class OncePerFrameGate<TKey> where TKey : class
	{
		private int _lastFrame = -1;
		private TKey _lastKey;

		public bool ShouldRun(TKey key)
		{
			int f = Time.frameCount;
			if (f == _lastFrame && ReferenceEquals(_lastKey, key)) return false;
			_lastFrame = f;
			_lastKey = key;
			return true;
		}

		public void Reset()
		{
			_lastFrame = -1;
			_lastKey = null;
		}
	}
}


