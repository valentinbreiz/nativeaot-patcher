using Cosmos.API.Attributes;

namespace Cosmos.NativeWrapper;

[Plug(typeof(TestClass))]
public class TestClassPlug
{
    [PlugMember]
    public static int Add(int a, int b) => a * b;

    [PlugMember]
    public static void OutputDebugString(object aThis)
    {
    }
}
