using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.Kernel.HAL.Devices.Graphic
{
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct SVGA3dTextureState
    {
        [FieldOffset(0)]
        public uint stage;
        [FieldOffset(4)]
        public SVGA3dTextureStateName state;
        [FieldOffset(8)]
        public uint value;
        [FieldOffset(8)]
        public float floatValue;

        public SVGA3dTextureState(SVGA3dTextureStateName State, uint value, uint stage = 0u)
        {
            this.stage = stage;
            this.state = State;
            this.floatValue = 0;
            this.value = value;
        }

        public SVGA3dTextureState(SVGA3dTextureStateName State, float value, uint stage = 0u)
        {
            this.stage = stage;
            this.state = State;
            this.value = 0;
            this.floatValue = value;
        }
    }
}
