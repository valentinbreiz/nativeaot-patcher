using System;

namespace Cosmos.Kernel.Runtime
{
    public interface IRuntimeHelpers
    {
        unsafe void AssignRef(void** location, void* value);
        unsafe void CheckedAssignRef(void** location, void* value);
        unsafe void ByRefAssignRef(object* location, object value);
        int Dbl2Int(double value);
    }
}