using Liquip.API.Attributes;
using System.Runtime.InteropServices;

namespace Liquip.NativeWrapper
{
    public class TestClass
    {
        [DllImport("Liquip.NativeLibrary.dll", EntryPoint = "Add", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Add(int a, int b);

        public static int NativeAdd(int a, int b)
        {
            return Add(a, b);
        }

        public static int ManagedAdd(int a, int b)
        {
            return (a + b);
        }
    }

    public class MockTarget { }

    public class NonPlug { }

    [Plug(typeof(MockTarget))]
    public class MockPlug { }

    [Plug(typeof(MockTarget))]
    public class EmptyPlug
    {
        // No methods defined
    }

    [Plug(typeof(MockTarget))]
    public class MockPlugWithMethods
    {
        public static void StaticMethod() { }
        public void InstanceMethod() { }
    }

    [Plug("OptionalTarget", IsOptional = true)]
    public class OptionalPlug { }
}
