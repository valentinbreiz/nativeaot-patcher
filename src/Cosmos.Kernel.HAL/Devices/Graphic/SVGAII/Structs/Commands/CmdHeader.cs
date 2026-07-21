using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dCmdHeader
    {
        public uint id;
        public uint size;
    }
