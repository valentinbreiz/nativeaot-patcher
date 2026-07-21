using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct SVGA3dCmdDefineSurface
    {
        public uint sid;
        public SVGA3dSurfaceFlags flags;
        public SVGA3dSurfaceFormat format;
        public fixed uint face[6];
    }
}
