using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct SVGA3dCmdSetTransform
    {
        public uint cid;
        public SVGA3dTransformType type;
        public fixed float matrix[16];
    }
