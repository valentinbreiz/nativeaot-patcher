using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;

public partial class X64PortIO : IPortIO
{
    [LibraryImport("*", EntryPoint = "_native_io_read_byte")]
    private static partial byte NativeReadByte(ushort port);

    [LibraryImport("*", EntryPoint = "_native_io_read_word")]
    private static partial ushort NativeReadWord(ushort port);

    [LibraryImport("*", EntryPoint = "_native_io_read_dword")]
    private static partial uint NativeReadDWord(ushort port);

    [LibraryImport("*", EntryPoint = "_native_io_write_byte")]
    private static partial void NativeWriteByte(ushort port, byte value);

    [LibraryImport("*", EntryPoint = "_native_io_write_word")]
    private static partial void NativeWriteWord(ushort port, ushort value);

    [LibraryImport("*", EntryPoint = "_native_io_write_dword")]
    private static partial void NativeWriteDWord(ushort port, uint value);

    public byte ReadByte(ushort port) => NativeReadByte(port);
    public ushort ReadWord(ushort port) => NativeReadWord(port);
    public uint ReadDWord(ushort port) => NativeReadDWord(port);

    public void WriteByte(ushort port, byte value) => NativeWriteByte(port, value);
    public void WriteWord(ushort port, ushort value) => NativeWriteWord(port, value);
    public void WriteDWord(ushort port, uint value) => NativeWriteDWord(port, value);
}
