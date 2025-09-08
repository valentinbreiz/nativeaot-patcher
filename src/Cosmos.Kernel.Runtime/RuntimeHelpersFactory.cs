using System;

namespace Cosmos.Kernel.Runtime
{
    public static class RuntimeHelpersFactory
    {
        private static IRuntimeHelpers? _instance;

        public static IRuntimeHelpers Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreatePlatformSpecific();
                }
                return _instance;
            }
        }

        private static IRuntimeHelpers CreatePlatformSpecific()
        {
#if ARM64
            return new ARM64RuntimeHelpers();
#else
            return new X64RuntimeHelpers();
#endif
        }
    }
}