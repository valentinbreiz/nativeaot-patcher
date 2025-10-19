// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.HAL.BlockDevice.IOGroup;

public class AtaIoGroup
{
    /// <summary>
    /// Error IOPort
    /// </summary>
    public readonly int Error; // BAR0 + 1 - read only

    /// <summary>
    /// Features IOPort.
    /// </summary>
    public readonly int Features; // BAR0 + 1 - write only

    /// <summary>
    /// Data IOPort.
    /// </summary>
    public readonly int Data; // BAR0

    /// <summary>
    /// Sector Count IOPort.
    /// </summary>
    public readonly int SectorCount; // BAR0 + 2

    /// <summary>
    /// LBA0 IOPort.
    /// </summary>
    public readonly int Lba0; // BAR0 + 3

    /// <summary>
    /// LBA1 IOPort.
    /// </summary>
    public readonly int Lba1; // BAR0 + 4

    /// <summary>
    /// LBA2 IOPort.
    /// </summary>
    public readonly int Lba2; // BAR0 + 5

    /// <summary>
    /// Device select IOPort.
    /// </summary>
    public readonly int DeviceSelect; // BAR0 + 6

    /// <summary>
    /// Command IOPort.
    /// </summary>
    public readonly int Command; // BAR0 + 7 - write only

    /// <summary>
    /// Status IOPort.
    /// </summary>
    public readonly int Status; // BAR0 + 7 - read only

    /// <summary>
    /// Sector count IOPort.
    /// </summary>
    public readonly int SectorCountLba48; // BAR0 + 8

    /// <summary>
    /// LBA3 IOPort.
    /// </summary>
    public readonly int Lba3; // BAR0 + 9

    /// <summary>
    /// LBA4 IOPort.
    /// </summary>
    public readonly int Lba4; // BAR0 + 10

    /// <summary>
    /// LBA5 IOPort.
    /// </summary>
    public readonly int Lba5; // BAR0 + 11

    /// <summary>
    /// Alternate Status IOPort.
    /// </summary>
    public readonly int AlternateStatus; // BAR1 + 2 - read only

    /// <summary>
    /// Control IOPort.
    /// </summary>
    public readonly int Control; // BAR1 + 2 - write only

    /// <summary>
    /// Constructor for ATA-spec device (including ATAPI?)
    /// aSecondary boolean to check if Primary or Secondary channel, used in modern ATA controllers
    /// </summary>
    /// <param name="aSecondary"></param>
    public AtaIoGroup(bool aSecondary)
    {
        if (aSecondary)
        {
            Serial.WriteString("Creating Secondary ATA IOGroup");
        }
        else
        {
            Serial.WriteString("Creating Primary ATA IOGroup");
        }

        int xBar0 = GetBar0(aSecondary);
        int xBar1 = GetBar1(aSecondary);
        Error = Features = xBar0 + 1;
        Data = xBar0;
        SectorCount = xBar0 + 2;
        Lba0 = xBar0 + 3;
        Lba1 = xBar0 + 4;
        Lba2 = xBar0 + 5;
        DeviceSelect = xBar0 + 6;
        Status = Command = xBar0 + 7;
        SectorCountLba48 = xBar0 + 8;
        Lba3 = xBar0 + 9;
        Lba4 = xBar0 + 10;
        Lba5 = xBar0 + 11;
        AlternateStatus = Control = xBar1 + 2;
    }

    /// <summary>
    /// Waits for IO operations to complete.
    /// </summary>
    public void Wait()
    {
        // Used for the PATA and IOPort latency
        // Widely accepted method is to read the status register 4 times - approx. 400ns delay.
        PlatformHAL.PortIO.WriteByte(0x80, 0x22);
        PlatformHAL.PortIO.WriteByte(0x80, 0x22);
        PlatformHAL.PortIO.WriteByte(0x80, 0x22);
        PlatformHAL.PortIO.WriteByte(0x80, 0x22);
    }

    /// <summary>
    /// Get control base address.
    /// </summary>
    /// <param name="aSecondary">True if secondary ATA.</param>
    /// <returns>ushort value.</returns>
    private static int GetBar1(bool aSecondary) => aSecondary ? 0x0374 : 0x03F4;

    /// <summary>
    /// Get command base address.
    /// </summary>
    /// <param name="aSecondary">True if secondary ATA.</param>
    /// <returns>ushort value.</returns>
    private static int GetBar0(bool aSecondary) => aSecondary ? 0x0170 : 0x01F0;
}
