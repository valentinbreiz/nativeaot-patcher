using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;

[PlatformSpecific(PlatformArchitecture.X64)]
public class X64PortIO : IPortIO
{
    [DllImport("*", EntryPoint = "_native_io_read_byte")]
    private static extern byte NativeReadByte(ushort port);

    [DllImport("*", EntryPoint = "_native_io_read_word")]
    private static extern ushort NativeReadWord(ushort port);

    [DllImport("*", EntryPoint = "_native_io_read_dword")]
    private static extern uint NativeReadDWord(ushort port);

    [DllImport("*", EntryPoint = "_native_io_write_byte")]
    private static extern void NativeWriteByte(ushort port, byte value);

    [DllImport("*", EntryPoint = "_native_io_write_word")]
    private static extern void NativeWriteWord(ushort port, ushort value);

    [DllImport("*", EntryPoint = "_native_io_write_dword")]
    private static extern void NativeWriteDWord(ushort port, uint value);

    public byte ReadByte(ushort port) => NativeReadByte(port);
    public ushort ReadWord(ushort port) => NativeReadWord(port);
    public uint ReadDWord(ushort port) => NativeReadDWord(port);

    public void WriteByte(ushort port, byte value) => NativeWriteByte(port, value);
    public void WriteWord(ushort port, ushort value) => NativeWriteWord(port, value);
    public void WriteDWord(ushort port, uint value) => NativeWriteDWord(port, value);
}
