using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SVGA3dGuestImage
    {
        public SVGAGuestPtr ptr;
        public float pitch;
    }
}
