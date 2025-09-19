using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.ARM64;

public partial class ARM64MemoryIO : IPortIO
{
    // ARM64 uses memory-mapped I/O instead of port I/O
    private const ulong MMIO_BASE = 0x3F000000; // Example base for Raspberry Pi 3/4

    [LibraryImport("*", EntryPoint = "_native_mmio_read_byte")]
    private static partial byte NativeReadByte(ulong address);

    [LibraryImport("*", EntryPoint = "_native_mmio_read_word")]
    private static partial ushort NativeReadWord(ulong address);

    [LibraryImport("*", EntryPoint = "_native_mmio_read_dword")]
    private static partial uint NativeReadDWord(ulong address);

    [LibraryImport("*", EntryPoint = "_native_mmio_write_byte")]
    private static partial void NativeWriteByte(ulong address, byte value);

    [LibraryImport("*", EntryPoint = "_native_mmio_write_word")]
    private static partial void NativeWriteWord(ulong address, ushort value);

    [LibraryImport("*", EntryPoint = "_native_mmio_write_dword")]
    private static partial void NativeWriteDWord(ulong address, uint value);

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
