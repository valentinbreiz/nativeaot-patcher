using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dCmdSetZRange
    {
        public uint cid;
        public SVGA3dZRange range;
    }
