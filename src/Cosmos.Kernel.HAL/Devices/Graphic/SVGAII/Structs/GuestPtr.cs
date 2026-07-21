using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGAGuestPtr
{
    public uint gmrId;
    public uint offset;
}
