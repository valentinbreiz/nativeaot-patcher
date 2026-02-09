// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Interface for shared memory devices (e.g., QEMU ivshmem).
/// Provides zero-pause bidirectional memory sharing between kernel and host.
/// </summary>
public interface ISharedMemoryDevice
{
    /// <summary>
    /// Gets the base address of the shared memory region.
    /// </summary>
    nint GetSharedMemory();

    /// <summary>
    /// Gets the size of the shared memory region in bytes.
    /// </summary>
    uint GetSharedMemorySize();
}
