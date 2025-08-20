using Cosmos.Build.API.Attributes;

namespace Cosmos.Tests.NativeWrapper;

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
