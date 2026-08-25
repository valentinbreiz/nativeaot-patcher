// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.Heap;

[StructLayout(LayoutKind.Sequential)]
public struct LargeHeapHeader
{
    public ulong Used;
    public uint Size;
}
