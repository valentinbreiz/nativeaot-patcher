namespace Cosmos.Kernel.HAL;

public interface IPortIO
{
    byte ReadByte(ushort port);
    ushort ReadWord(ushort port);
    uint ReadDWord(ushort port);

    void WriteByte(ushort port, byte value);
    void WriteWord(ushort port, ushort value);
    void WriteDWord(ushort port, uint value);
}
