using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
namespace Cosmos.Kernel.HAL;

[PlatformSpecific(PlatformArchitecture.ARM64)]
public class ARM64MemoryIO : IPortIO
{
    // ARM64 uses memory-mapped I/O instead of port I/O
    private const ulong MMIO_BASE = 0x3F000000; // Example base for Raspberry Pi 3/4

    [DllImport("*", EntryPoint = "_native_mmio_read_byte")]
    private static extern byte NativeReadByte(ulong address);

    [DllImport("*", EntryPoint = "_native_mmio_read_word")]
    private static extern ushort NativeReadWord(ulong address);

    [DllImport("*", EntryPoint = "_native_mmio_read_dword")]
    private static extern uint NativeReadDWord(ulong address);

    [DllImport("*", EntryPoint = "_native_mmio_write_byte")]
    private static extern void NativeWriteByte(ulong address, byte value);

    [DllImport("*", EntryPoint = "_native_mmio_write_word")]
    private static extern void NativeWriteWord(ulong address, ushort value);

    [DllImport("*", EntryPoint = "_native_mmio_write_dword")]
    private static extern void NativeWriteDWord(ulong address, uint value);

    private static ulong PortToAddress(ushort port)
    {
        // Map legacy x86 port numbers to ARM64 MMIO addresses
        return MMIO_BASE + port;
    }

    public byte ReadByte(ushort port) => NativeReadByte(PortToAddress(port));
    public ushort ReadWord(ushort port) => NativeReadWord(PortToAddress(port));
    public uint ReadDWord(ushort port) => NativeReadDWord(PortToAddress(port));

    public void WriteByte(ushort port, byte value) => NativeWriteByte(PortToAddress(port), value);
    public void WriteWord(ushort port, ushort value) => NativeWriteWord(PortToAddress(port), value);
    public void WriteDWord(ushort port, uint value) => NativeWriteDWord(PortToAddress(port), value);
}
