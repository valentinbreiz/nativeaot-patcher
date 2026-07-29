// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Network;

/// <summary>
/// Manages network devices.
/// </summary>
public static class NetworkManager
{
    /// <summary>
    /// Whether network support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.NetworkEnabled;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Network support is disabled. Set CosmosEnableNetwork=true in your csproj to enable it.");
        }
    }

    private static INetworkDevice? s_primaryDevice;
    private static INetworkDevice?[]? s_devices;
    private static int s_deviceCount;
    private static bool s_initialized;

    /// <summary>
    /// Gets whether the network manager is initialized.
    /// </summary>
    public static bool IsInitialized => s_initialized;

    /// <summary>
    /// Gets the primary network device.
    /// </summary>
    public static INetworkDevice? PrimaryDevice => s_primaryDevice;

    /// <summary>
    /// Gets the number of registered network devices.
    /// </summary>
    public static int DeviceCount => s_deviceCount;

    /// <summary>
    /// Initializes the network manager.
    /// </summary>
    public static void Initialize()
    {
        ThrowIfDisabled();

        if (s_initialized)
        {
            return;
        }

        s_devices = new INetworkDevice[8];
        s_deviceCount = 0;
        s_initialized = true;
    }

    /// <summary>
    /// Registers a network device with the manager.
    /// </summary>
    /// <param name="device">The network device to register.</param>
    public static void RegisterDevice(INetworkDevice device)
    {
        if (device == null || s_devices == null || s_deviceCount >= s_devices.Length)
        {
            return;
        }

        s_devices[s_deviceCount++] = device;

        // First device becomes primary
        if (s_primaryDevice == null)
        {
            s_primaryDevice = device;
        }
    }

    /// <summary>
    /// Gets a network device by index.
    /// </summary>
    /// <param name="index">The device index.</param>
    /// <returns>The network device, or null if not found.</returns>
    public static INetworkDevice? GetDevice(int index)
    {
        ThrowIfDisabled();

        if (s_devices == null || index < 0 || index >= s_deviceCount)
        {
            return null;
        }

        return s_devices[index];
    }

    /// <summary>
    /// Sends a packet using the primary network device.
    /// </summary>
    /// <param name="data">The packet data.</param>
    /// <param name="length">The packet length.</param>
    /// <returns>True if the packet was sent successfully.</returns>
    public static bool Send(byte[] data, int length)
    {
        ThrowIfDisabled();
        return s_primaryDevice?.Send(data, length) ?? false;
    }

    /// <summary>
    /// Gets whether the primary device link is up.
    /// </summary>
    public static bool LinkUp => s_primaryDevice?.LinkUp ?? false;
}
