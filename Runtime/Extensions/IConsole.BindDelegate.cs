// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	using System;

	public static partial class IConsole_
	{
		public static CommandHandle BindDelegate<DT>(this IConsole console, DT d, string name, string description = null) where DT : Delegate
		{
			return console.Bind(name, d.Method, d.Target, description);
		}
	}
}