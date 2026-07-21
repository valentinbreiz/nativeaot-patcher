using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dCmdSetShader
    {
        public uint cid;
        public SVGA3dShaderType type;
        public uint shid;
    }
