// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.Services.Network;

/// <summary>
/// Manages network devices.
/// </summary>
public static class NetworkManager
{
    private static INetworkDevice? _primaryDevice;
    private static INetworkDevice?[]? _devices;
    private static int _deviceCount;
    private static bool _initialized;

    /// <summary>
    /// Gets whether the network manager is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Gets the primary network device.
    /// </summary>
    public static INetworkDevice? PrimaryDevice => _primaryDevice;

    /// <summary>
    /// Gets the number of registered network devices.
    /// </summary>
    public static int DeviceCount => _deviceCount;

    /// <summary>
    /// Initializes the network manager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _devices = new INetworkDevice[8];
        _deviceCount = 0;
        _initialized = true;
    }

    /// <summary>
    /// Registers a network device with the manager.
    /// </summary>
    /// <param name="device">The network device to register.</param>
    public static void RegisterDevice(INetworkDevice device)
    {
        if (device == null || _devices == null || _deviceCount >= _devices.Length)
            return;

        _devices[_deviceCount++] = device;

        // First device becomes primary
        if (_primaryDevice == null)
        {
            _primaryDevice = device;
        }
    }

    /// <summary>
    /// Gets a network device by index.
    /// </summary>
    /// <param name="index">The device index.</param>
    /// <returns>The network device, or null if not found.</returns>
    public static INetworkDevice? GetDevice(int index)
    {
        if (_devices == null || index < 0 || index >= _deviceCount)
            return null;

        return _devices[index];
    }

    /// <summary>
    /// Sends a packet using the primary network device.
    /// </summary>
    /// <param name="data">The packet data.</param>
    /// <param name="length">The packet length.</param>
    /// <returns>True if the packet was sent successfully.</returns>
    public static bool Send(byte[] data, int length)
    {
        return _primaryDevice?.Send(data, length) ?? false;
    }

    /// <summary>
    /// Gets whether the primary device link is up.
    /// </summary>
    public static bool LinkUp => _primaryDevice?.LinkUp ?? false;
}
