using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Tests.NativeWrapper;

public class TestClass
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int OutputDebugString(string lpOutputString);

    [DllImport("Cosmos.NativeLibrary.dll", EntryPoint = "Add", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Add(int a, int b);


    [UnmanagedCallersOnly(EntryPoint = "Native_Add", CallConvs = [typeof(CallConvCdecl)])]
    public static int NativeAdd(int a, int b) =>
        // _ = OutputDebugString("NativeAdd method called");
        Add(a, b);

    public static int ManagedAdd(int a, int b) =>
        // OutputDebugString("ManagedAdd method called");
        a + b;
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
    [PlugMember]
    public static void StaticMethod()
    {
    }

    [PlugMember]
    public void InstanceMethod()
    {
    }
}

[Plug("OptionalTarget", IsOptional = true)]
public class OptionalPlug
{
}
