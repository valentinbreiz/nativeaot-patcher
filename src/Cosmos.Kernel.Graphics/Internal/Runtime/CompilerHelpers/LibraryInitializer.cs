using Cosmos.Kernel.Graphics;

namespace Internal.Runtime.CompilerHelpers
{
    internal static class LibraryInitializer
    {
        public static void InitializeLibrary()
        {
            KernelConsole.Initialize();
        }
    }
}
