using Liquip.API.Attributes;

namespace Liquip.NativeLibrary.Tests.PlugSample
{
	[Plug(typeof(NativeWrapper))]
	public static class NativeWrapperPlug
	{
		public static int Add(int a, int b)
		{
			return a + b;
		}
	}
}
