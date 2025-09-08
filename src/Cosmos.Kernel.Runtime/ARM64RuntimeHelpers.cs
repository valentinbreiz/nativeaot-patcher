using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Runtime
{
    [PlatformSpecific(PlatformArchitecture.ARM64)]
    public class ARM64RuntimeHelpers : IRuntimeHelpers
    {
        [DllImport("*", EntryPoint = "RhpAssignRefArm64")]
        private static extern unsafe void NativeAssignRef(void** location, void* value);

        [DllImport("*", EntryPoint = "RhpCheckedAssignRefArm64")]
        private static extern unsafe void NativeCheckedAssignRef(void** location, void* value);

        [DllImport("*", EntryPoint = "RhpByRefAssignRefArm64")]
        private static extern unsafe void NativeByRefAssignRef(object* location, object value);

        public unsafe void AssignRef(void** location, void* value) => NativeAssignRef(location, value);
        public unsafe void CheckedAssignRef(void** location, void* value) => NativeCheckedAssignRef(location, value);
        public unsafe void ByRefAssignRef(object* location, object value) => NativeByRefAssignRef(location, value);
        public int Dbl2Int(double value) => (int)value;
    }
}