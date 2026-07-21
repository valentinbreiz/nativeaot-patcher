using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct SVGA3dCmdSetShaderConst
    {
        public uint cid;
        public uint reg;
        public SVGA3dShaderType type;
        public SVGA3dShaderConstType ctype;
        public fixed uint values[4];
    }
}
