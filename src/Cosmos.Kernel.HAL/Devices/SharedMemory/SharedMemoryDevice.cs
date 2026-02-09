// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.SharedMemory;

/// <summary>
/// Abstract base class for shared memory devices (e.g., QEMU ivshmem).
/// Provides zero-pause bidirectional memory sharing between kernel and host.
/// </summary>
public abstract class SharedMemoryDevice : Device, ISharedMemoryDevice
{
    private static SharedMemoryDevice? s_instance;

    /// <summary>
    /// Gets the currently registered shared memory device instance.
    /// Returns null if no device is available.
    /// </summary>
    public static SharedMemoryDevice? Instance => s_instance;

    /// <summary>
    /// Registers this device as the active shared memory device.
    /// Should be called by concrete implementations after successful initialization.
    /// </summary>
    protected void Register()
    {
        s_instance = this;
    }

    /// <summary>
    /// Gets the base address of the shared memory region.
    /// </summary>
    public abstract nint GetSharedMemory();

    /// <summary>
    /// Gets the size of the shared memory region in bytes.
    /// </summary>
    public abstract uint GetSharedMemorySize();
}
