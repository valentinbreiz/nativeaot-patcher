using Liquip.API.Attributes;
using System.Runtime.InteropServices;

namespace Liquip.NativeWrapper;

public class TestClass
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int OutputDebugString(string lpOutputString);

    [DllImport("Liquip.NativeLibrary.dll", EntryPoint = "Add", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Add(int a, int b);

    public static int NativeAdd(int a, int b)
    {
        OutputDebugString("NativeAdd method called");

        return Add(a, b);
    }

    public static int ManagedAdd(int a, int b)
    {
        OutputDebugString("ManagedAdd method called");

        return a + b;
    }
}

public class MockTarget
{
}

public class NonPlug
{
}

[Plug(typeof(MockTarget))]
public class MockPlug
{
}

[Plug(typeof(MockTarget))]
public class EmptyPlug
{
    // No methods defined
}

[Plug(typeof(MockTarget))]
public class MockPlugWithMethods
{
    public static void StaticMethod()
    {
    }

    public void InstanceMethod()
    {
    }
}

[Plug("OptionalTarget", IsOptional = true)]
public class OptionalPlug
{
}
