using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Tests.NativeWrapper;

public partial class TestClass
{
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int OutputDebugString(string lpOutputString);

    [LibraryImport("Cosmos.Test.NativeLibrary.dll", EntryPoint = "Add")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Add(int a, int b);


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
