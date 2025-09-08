using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;

[PlatformSpecific(PlatformArchitecture.X64)]
public class X64PortIO : IPortIO
{
    [DllImport("*", EntryPoint = "_native_io_read_byte")]
    private static extern byte NativeReadByte(ushort port);

    [DllImport("*", EntryPoint = "_native_io_write_byte")]
    private static extern void NativeWriteByte(ushort port, byte value);

    public byte ReadByte(ushort port) => NativeReadByte(port);
    public ushort ReadWord(ushort port) => 0; // Simplified - not implemented
    public uint ReadDWord(ushort port) => 0; // Simplified - not implemented

    public void WriteByte(ushort port, byte value) => NativeWriteByte(port, value);
    public void WriteWord(ushort port, ushort value) { } // Simplified - not implemented
    public void WriteDWord(ushort port, uint value) { } // Simplified - not implemented
}
