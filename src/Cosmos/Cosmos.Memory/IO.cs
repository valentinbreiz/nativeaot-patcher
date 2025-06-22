// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;

namespace Cosmos.Memory;

public static class IO
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_write_byte")]
    public static extern void Write8(ushort port, byte value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_write_word")]
    public static extern void Write16(ushort port, ushort value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_write_dword")]
    public static extern void Write32(ushort port, uint value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_read_byte")]
    public static extern byte Read8(ushort port);

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_read_word")]
    public static extern ushort Read16(ushort port);

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "_native_io_read_dword")]
    public static extern uint Read32(ushort port);
}
