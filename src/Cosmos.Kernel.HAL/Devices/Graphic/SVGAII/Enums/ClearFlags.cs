using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [Flags]
    public enum ClearFlags : uint
    {
        Color = 0x1,
        Depth = 0x2,
        Stencil = 0x4
    }
}
