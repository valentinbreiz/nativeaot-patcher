// This code is licensed under MIT license (see LICENSE for details)

// ReSharper disable once CheckNamespace

using Cosmos.Kernel.Core.IO;

namespace Internal.Runtime.CompilerHelpers
{
    public class LibraryInitializer
    {
        public static void InitializeLibrary()
        {
            Serial.StartThread();
        }
    }
}
