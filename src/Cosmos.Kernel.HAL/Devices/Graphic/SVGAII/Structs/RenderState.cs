using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct SVGA3dRenderState
    {
        [FieldOffset(0)]
        public SVGA3dRenderStateName state;
        [FieldOffset(4)]
        public uint uintValue;
        [FieldOffset(4)]
        public float floatValue;

        public SVGA3dRenderState(SVGA3dRenderStateName State, uint value)
        {
            this.state = State;
            this.floatValue = 0;
            this.uintValue = value;
        }

        public SVGA3dRenderState(SVGA3dRenderStateName State, float value)
        {
            this.state = State;
            this.uintValue = 0;
            this.floatValue = value;
        }
    }
}
