// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	using UnityEngine.UIElements;

	internal static class UIElements_
	{
		public static void AddClasses(this VisualElement ve, params string[] classes)
		{
			foreach (var cls in classes)
			{
				ve.AddToClassList(cls);
			}
		}
	}
}