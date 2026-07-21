using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    public enum SVGA3dPrimitiveType
    {
        SVGA3D_PRIMITIVE_INVALID = 0,
        SVGA3D_PRIMITIVE_TRIANGLELIST = 1,
        SVGA3D_PRIMITIVE_POINTLIST = 2,
        SVGA3D_PRIMITIVE_LINELIST = 3,
        SVGA3D_PRIMITIVE_LINESTRIP = 4,
        SVGA3D_PRIMITIVE_TRIANGLESTRIP = 5,
        SVGA3D_PRIMITIVE_TRIANGLEFAN = 6,
        SVGA3D_PRIMITIVE_MAX
    }
}
