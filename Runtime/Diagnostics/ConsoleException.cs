// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	using System;

	public class ConsoleException : Exception
	{
		public ConsoleException() { }
		public ConsoleException(string msg) : base(msg) { }
		public ConsoleException(string msg, Exception inner) : base(msg, inner)
		{
		}
	}
}