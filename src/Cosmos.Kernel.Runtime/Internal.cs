using System;
using System.Runtime;
using System.Runtime.CompilerServices;
namespace Cosmos.Kernel.Runtime;

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

    public static class Cpu
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_lidt")]
        public static extern unsafe void Lidt(void* baseAddress, ushort size);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_sti")]
        public static extern void Sti();
    }
}
