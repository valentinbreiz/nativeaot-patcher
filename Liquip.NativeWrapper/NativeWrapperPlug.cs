using Liquip.API.Attributes;

namespace NativeWrapper
{
	[Plug(typeof(TestClass))]
	public class TestClassPlug
    {
		public static int Add(int a, int b)
		{
			return a * b;
		}
	}
}
