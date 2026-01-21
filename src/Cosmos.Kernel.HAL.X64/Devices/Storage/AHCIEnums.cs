// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// AHCI base memory address constant.
/// </summary>
public static class AHCIBase
{
    public const uint AHCI = 0x00400000;
}

/// <summary>
/// Port type enumeration.
/// </summary>
public enum PortType
{
    Nothing = 0x00,
    SATA = 0x01,
    SATAPI = 0x02,
    SEMB = 0x03,
    PM = 0x04
}

/// <summary>
/// FIS (Frame Information Structure) type enumeration.
/// </summary>
public enum FISType : byte
{
    FIS_Type_RegisterH2D = 0x27,  // Register FIS: Host to Device
    FIS_Type_RegisterD2H = 0x34,  // Register FIS: Device to Host
    FIS_Type_DMA_Activate = 0x39, // DMA Activate
    FIS_Type_DMA_Setup = 0x41,    // DMA Setup: Device to Host
    FIS_Type_Data = 0x46,         // Data FIS: Bidirectional
    FIS_Type_BIST = 0x58,         // BIST
    FIS_Type_PIO_Setup = 0x5F,    // PIO Setup: Device to Host
    FIS_Type_DeviceBits = 0xA1    // Device bits
}

/// <summary>
/// AHCI drive signature to identify what drive is plugged to Port.
/// </summary>
public enum AHCISignature : uint
{
    SATA = 0x0000,
    PortMultiplier = 0x9669,
    SATAPI = 0xEB14,
    SEMB = 0xC33C,
    Nothing = 0xFFFF
}

/// <summary>
/// SATA Status: Interface Power Management Status.
/// </summary>
public enum InterfacePowerManagementStatus : uint
{
    NotPresent = 0x00,
    Active = 0x01,
    Partial = 0x02,
    Slumber = 0x06,
    DeviceSleep = 0x08
}

/// <summary>
/// SATA Status: Current Interface Speed.
/// </summary>
public enum CurrentInterfaceSpeedStatus : uint
{
    NotPresent = 0x00,
    Gen1Rate = 0x01,
    Gen2Rate = 0x02,
    Gen3Rate = 0x03
}

/// <summary>
/// SATA Status: Device Detection Status.
/// </summary>
public enum DeviceDetectionStatus : uint
{
    NotDetected = 0x00,
    DeviceDetectedNoPhy = 0x01,
    DeviceDetectedWithPhy = 0x03,
    PhyOffline = 0x04
}

/// <summary>
/// ATA Device Status bits.
/// </summary>
public enum ATADeviceStatus : uint
{
    Busy = 0x80,
    DRQ = 0x08
}

/// <summary>
/// Command and Status register bits.
/// </summary>
public enum CommandAndStatus : uint
{
    ICC_Reserved0 = 0x0000000F,
    ICC_DevSleep = 0x00000008,
    ICC_Slumber = 0x00000006,
    ICC_Partial = 0x00000002,
    ICC_Active = 0x00000001,
    ICC_Idle = 0x00000000,
    ASP = 01 << 27,
    ALPE = 01 << 26,
    EnableATAPILED = 01 << 25,
    ATAPIDevice = 01 << 24,
    APSTE = 01 << 23,
    FISSwitchPort = 01 << 22,
    ExternalSATAPort = 01 << 21,
    ColdPresenceDetect = 01 << 20,
    MPSP = 01 << 19,
    HotPlugCapPort = 01 << 18,
    PortMultAttach = 01 << 17,
    ColdPresenceState = 01 << 16,
    CMDListRunning = 01 << 15,
    FISRecieveRunning = 01 << 14,
    MPSS = 01 << 13,
    CurrentCMDSlot = 01 << 12,
    Reserved0 = 01 << 07,
    FISRecieveEnable = 01 << 04,
    CMDListOverride = 01 << 03,
    PowerOnDevice = 01 << 02,
    SpinUpDevice = 01 << 01,
    StartProccess = 01 << 00,
    Null = 0xFFFF
}

/// <summary>
/// Interrupt Status bits.
/// </summary>
public enum InterruptStatus : int
{
    ColdPortDetectStatus = 01 << 31,
    TaskFileErrorStatus = 01 << 30,
    HostBusFatalErrorStatus = 01 << 29,
    HostBusDataErrorStatus = 01 << 28,
    InterfaceFatalErrorStatus = 01 << 27,
    InterfaceNFatalErrorStatus = 01 << 26,
    OverflowStatus = 01 << 24,
    IncorrectPMStatus = 01 << 23,
    PhyRdyChangeStatus = 01 << 22,
    DevMechanicalPresenceStatus = 01 << 07,
    PortConnectChangeStatus = 01 << 06,
    DescriptorProcessed = 01 << 05,
    UnknownFISInterrupt = 01 << 04,
    SetDeviceBitsInterrupt = 01 << 03,
    DMASetupFISInterrupt = 01 << 02,
    PIOSetupFISInterrupt = 01 << 01,
    D2HRegFISInterrupt = 01 << 00,
    Null = 0xFFFF
}

/// <summary>
/// ATA Commands.
/// </summary>
public enum ATACommands : byte
{
    ReadDma = 0xC8,
    ReadDmaExt = 0x25,
    WriteDma = 0xCA,
    WriteDmaExt = 0x35,
    CacheFlush = 0xE7,
    CacheFlushExt = 0xEA,
    Packet = 0xA0,
    IdentifyPacket = 0xA1,
    IdentifyDMA = 0xEE,
    Identify = 0xEC,
    Read = 0xA8,
    Eject = 0x1B
}