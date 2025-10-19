// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.BlockDevice;

public abstract class Ata : BlockDevice
{
    protected Ata() => BlockSize = 512;

    // In future may need to add a None for PCI ATA controllers.
    // Or maybe they all have Primary and Secondary on them as well.
    public enum ControllerIdEnum
    {
        Primary,
        Secondary
    }

    protected ControllerIdEnum mControllerID;
    public ControllerIdEnum ControllerID => mControllerID;

    public enum BusPositionEnum
    {
        Master,
        Slave
    }

    protected BusPositionEnum mBusPosition;
    public BusPositionEnum BusPosition => mBusPosition;

    public override string ToString() => "Ata (Abstract)";
}
