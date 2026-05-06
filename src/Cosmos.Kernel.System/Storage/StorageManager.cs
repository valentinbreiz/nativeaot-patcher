// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// Manages block storage devices.
/// </summary>
public static class StorageManager
{
    /// <summary>
    /// Whether storage support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.StorageEnabled;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Storage support is disabled. Set CosmosEnableStorage=true in your csproj to enable it.");
        }
    }

    private static IBlockDevice? _primaryDevice;
    private static IBlockDevice?[]? _devices;
    private static int _deviceCount;
    private static bool _initialized;

    /// <summary>
    /// Gets whether the storage manager is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Gets the primary block device (first one registered).
    /// </summary>
    public static IBlockDevice? PrimaryDevice => _primaryDevice;

    /// <summary>
    /// Gets the number of registered block devices.
    /// </summary>
    public static int DeviceCount => _deviceCount;

    /// <summary>
    /// Initializes the storage manager.
    /// </summary>
    public static void Initialize()
    {
        ThrowIfDisabled();

        if (_initialized)
        {
            return;
        }

        _devices = new IBlockDevice[8];
        _deviceCount = 0;
        _initialized = true;
    }

    /// <summary>
    /// Registers a block device with the manager.
    /// </summary>
    /// <param name="device">The block device to register.</param>
    public static void RegisterDevice(IBlockDevice device)
    {
        if (device == null || _devices == null || _deviceCount >= _devices.Length)
        {
            return;
        }

        _devices[_deviceCount++] = device;

        // First device becomes primary
        if (_primaryDevice == null)
        {
            _primaryDevice = device;
        }
    }

    /// <summary>
    /// Gets a block device by index.
    /// </summary>
    /// <param name="index">The device index.</param>
    /// <returns>The block device, or null if not found.</returns>
    public static IBlockDevice? GetDevice(int index)
    {
        ThrowIfDisabled();

        if (_devices == null || index < 0 || index >= _deviceCount)
        {
            return null;
        }

        return _devices[index];
    }
}
