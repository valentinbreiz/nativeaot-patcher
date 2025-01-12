using Liquip.API.Attributes;

namespace Liquip.NativeWrapper;

[Plug(typeof(TestClass))]
public class TestClassPlug
{
    public static int Add(int a, int b) => a * b;
}
