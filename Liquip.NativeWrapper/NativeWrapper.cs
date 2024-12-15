using System.Runtime.InteropServices;

namespace Liquip.NativeLibrary.Tests.PlugSample
{
    public static class NativeWrapper
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
}
