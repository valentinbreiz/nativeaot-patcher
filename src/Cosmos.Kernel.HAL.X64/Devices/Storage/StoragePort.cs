// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Devices.Storage;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// Abstract base class for AHCI storage ports (SATA, SATAPI, etc.).
/// </summary>
public abstract class StoragePort : BlockDevice
{
    /// <summary>
    /// The port type (SATA, SATAPI, etc.).
    /// </summary>
    public abstract PortType PortType { get; }

    /// <summary>
    /// The port name.
    /// </summary>
    public abstract string PortName { get; }

    /// <summary>
    /// The port number on the AHCI controller.
    /// </summary>
    public abstract uint PortNumber { get; }

    /// <inheritdoc />
    public override string Name => PortName;
}
