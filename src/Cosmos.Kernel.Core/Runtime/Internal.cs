using System.Runtime;
using System.Runtime.CompilerServices;
namespace Cosmos.Kernel.Core.Runtime;

public static class Native
{
    public static class IO
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_write_byte")]
        public static extern void Write8(ushort Port, byte Value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_write_word")]
        public static extern void Write16(ushort Port, ushort Value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_write_dword")]
        public static extern void Write32(ushort Port, uint Value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_read_byte")]
        public static extern byte Read8(ushort Port);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_read_word")]
        public static extern ushort Read16(ushort Port);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_io_read_dword")]
        public static extern uint Read32(ushort Port);
    }

    public static class Debug
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_debug_breakpoint")]
        public static extern void Breakpoint();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_debug_breakpoint_soft")]
        public static extern void BreakpointSoft();
    }
}
