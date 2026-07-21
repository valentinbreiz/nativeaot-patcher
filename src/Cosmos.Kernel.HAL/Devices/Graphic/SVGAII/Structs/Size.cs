using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dSize
    {
        public uint width;
        public uint height;
        public uint depth;
    }
