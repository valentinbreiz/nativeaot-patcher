using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct SVGA3dCmdSetTransform
    {
        public uint cid;
        public SVGA3dTransformType type;
        public fixed float matrix[16];
    }
}
