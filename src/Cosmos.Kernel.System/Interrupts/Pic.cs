using Cosmos.Kernel.Runtime;

namespace Cosmos.Kernel.System.Interrupts;

public static class Pic
{
    private const byte ICW1 = 0x11;
    private const byte ICW4 = 0x01;
    private const ushort MasterCmd = 0x20;
    private const ushort MasterData = 0x21;
    private const ushort SlaveCmd = 0xA0;
    private const ushort SlaveData = 0xA1;

    public static void Initialize()
    {
        Native.IO.Write8(MasterCmd, ICW1);
        Native.IO.Write8(SlaveCmd, ICW1);
        Native.IO.Write8(MasterData, 0x20);
        Native.IO.Write8(SlaveData, 0x28);
        Native.IO.Write8(MasterData, 0x04);
        Native.IO.Write8(SlaveData, 0x02);
        Native.IO.Write8(MasterData, ICW4);
        Native.IO.Write8(SlaveData, ICW4);
        Native.IO.Write8(MasterData, 0xFD);
        Native.IO.Write8(SlaveData, 0xFF);
    }

    public static void SendEoi(byte irq)
    {
        if (irq >= 8)
        {
            Native.IO.Write8(SlaveCmd, 0x20);
        }
        Native.IO.Write8(MasterCmd, 0x20);
    }
}
