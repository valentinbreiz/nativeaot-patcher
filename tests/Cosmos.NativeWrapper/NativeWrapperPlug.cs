using Cosmos.API.Attributes;

namespace Cosmos.NativeWrapper;

[Plug(typeof(TestClass))]
public class TestClassPlug
{
    public static int Add(int a, int b) => a * b;
}
