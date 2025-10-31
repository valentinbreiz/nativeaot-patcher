using System.Runtime;
using System.Runtime.CompilerServices;
namespace Cosmos.Kernel.Core.Runtime;

public static class Native
{
    public static unsafe class IO
    {
#if ARCH_ARM64
        // ARM64: Pure C# MMIO implementation using pointers
        // PL011 UART0 base address for QEMU virt machine
        private const ulong UART0_BASE = 0x09000000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write8(ushort Port, byte Value)
        {
            // Map COM1 port to PL011 UART data register (UARTDR at offset 0x00)
            *(byte*)UART0_BASE = Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write16(ushort Port, ushort Value)
        {
            *(ushort*)UART0_BASE = Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write32(ushort Port, uint Value)
        {
            *(uint*)UART0_BASE = Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Read8(ushort Port)
        {
            // Check if reading status register (port offset 5)
            if ((Port & 0xF) == 5)
            {
                // Read UART Flag Register at offset 0x18
                uint flags = *(uint*)(UART0_BASE + 0x18);
                // Convert PL011 UARTFR to x86 LSR format
                // PL011 bit 5 = TX FIFO full (1 = full, 0 = not full)
                // x86 LSR bit 5 = TX ready (1 = ready, 0 = not ready)
                return (flags & (1 << 5)) != 0 ? (byte)0 : (byte)0x20;
            }
            return *(byte*)UART0_BASE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Read16(ushort Port)
        {
            return *(ushort*)UART0_BASE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Read32(ushort Port)
        {
            return *(uint*)UART0_BASE;
        }

        // Direct MMIO access for PL011 initialization
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(ulong Address, byte Value)
        {
            *(byte*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteDWord(ulong Address, uint Value)
        {
            *(uint*)Address = Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadByte(ulong Address)
        {
            return *(byte*)Address;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadDWord(ulong Address)
        {
            return *(uint*)Address;
        }
#else
        // x86-64: Port I/O still needs assembly (can't do port I/O in C#)
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

        // MMIO not used on x86-64 (uses port I/O instead)
        public static void WriteByte(ulong Address, byte Value) => throw new System.NotSupportedException();
        public static void WriteDWord(ulong Address, uint Value) => throw new System.NotSupportedException();
        public static byte ReadByte(ulong Address) => throw new System.NotSupportedException();
        public static uint ReadDWord(ulong Address) => throw new System.NotSupportedException();
#endif
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

    public static class Uart
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "_native_uart_initialize")]
        public static extern void Initialize();
    }
}
