// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	using System;

	/// <summary>
	/// Reference to bound console handler
	/// </summary>
	public sealed class CommandHandle
	{
		public static readonly CommandHandle Empty = new(0, null);

		public bool IsValid => _unbindFn != null;
		internal ulong Key { get; private set; }

		public void Unbind()
		{
			_unbindFn?.Invoke(Key);
			_unbindFn = null;
		}

		internal void Invalidate()
		{
			_unbindFn = null;
			Key = 0;
		}

		internal CommandHandle(ulong key, Action<ulong> unbindFn)
		{
			Key = key;
			_unbindFn = unbindFn;
		}

		private Action<ulong> _unbindFn;
	}
}