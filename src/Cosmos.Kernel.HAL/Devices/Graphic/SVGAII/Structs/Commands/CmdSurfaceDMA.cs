using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dCmdSurfaceDMA
    {
        public SVGA3dGuestImage guest;
        public SVGA3dSurfaceImageId host;
        public SVGA3dTransferType transfer;
    }
}
