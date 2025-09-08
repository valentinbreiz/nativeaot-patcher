using System;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Runtime
{
    [PlatformSpecific(PlatformArchitecture.X64)]
    public class X64RuntimeHelpers : IRuntimeHelpers
    {
        public unsafe void AssignRef(void** location, void* value)
        {
            *location = value;
        }

        public unsafe void CheckedAssignRef(void** location, void* value)
        {
            *location = value;
        }

        public unsafe void ByRefAssignRef(object* location, object value)
        {
            *location = value;
        }

        public int Dbl2Int(double value)
        {
            return (int)value;
        }
    }
}