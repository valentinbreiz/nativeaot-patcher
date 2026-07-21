using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dArray
    {
        public uint surfaceId;
        public uint offset;
        public uint stride;
    }
}
