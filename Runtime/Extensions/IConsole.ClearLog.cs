// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	public static partial class IConsole_
	{
		public static void ClearLog(this IConsole console)
		{
			console?.Log?.ClearLog();
		}
	}
}