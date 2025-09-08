using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.ARM64;

[PlatformSpecific(PlatformArchitecture.ARM64)]
public class ARM64MemoryIO : IPortIO
{
    // ARM64 uses memory-mapped I/O instead of port I/O
    private const ulong MMIO_BASE = 0x3F000000; // Example base for Raspberry Pi 3/4
    
    [DllImport("*", EntryPoint = "_native_mmio_read_byte")]
    private static extern byte NativeReadByte(ulong address);
    
    [DllImport("*", EntryPoint = "_native_mmio_write_byte")]
    private static extern void NativeWriteByte(ulong address, byte value);
    
    private static ulong PortToAddress(ushort port)
    {
        // Map legacy x86 port numbers to ARM64 MMIO addresses
        return MMIO_BASE + port;
    }
    
    public byte ReadByte(ushort port) => NativeReadByte(PortToAddress(port));
    public ushort ReadWord(ushort port) => 0; // Simplified - not implemented
    public uint ReadDWord(ushort port) => 0; // Simplified - not implemented
    
    public void WriteByte(ushort port, byte value) => NativeWriteByte(PortToAddress(port), value);
    public void WriteWord(ushort port, ushort value) { } // Simplified - not implemented
    public void WriteDWord(ushort port, uint value) { } // Simplified - not implemented
}