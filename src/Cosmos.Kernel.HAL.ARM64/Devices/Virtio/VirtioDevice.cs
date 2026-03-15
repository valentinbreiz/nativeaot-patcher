using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.ARM64.Devices.Input;
using Cosmos.Kernel.HAL.ARM64.Devices.Network;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using static Cosmos.Kernel.HAL.ARM64.Devices.Virtio.VirtioMMIO;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Virtio;

/// <summary>
/// Provides initialization and management for virtio MMIO devices.
/// Handles device discovery, initialization handshake, and registration.
/// </summary>
// Note: This class is eagerly constructed at startup because accesing s_virtioDevices cause issues otherwise.
[EagerStaticClassConstruction]
public static class VirtioDevice
{
    // Linux input event types
    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;
    private const ushort EV_ABS = 0x03;
    private static readonly object?[] s_virtioDevices = new object?[VIRTIO_MMIO_MAX_DEVICES];

    /// <summary>object
    /// Retrieves a registered virtio device by its IRQ number.
    /// </summary>
    /// <typeparam name="T">The type of device to retrieve.</typeparam>
    /// <param name="irq">The IRQ number of the device.</param>
    /// <returns>The device instance if found and of the correct type, null otherwise.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown if the IRQ is invalid.</exception>
    public static T? GetDeviceFromIRQ<T>(ulong irq) where T : class
    {
        ulong index = irq - VIRTIO_IRQ_BASE;

        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual<ulong>(index, VIRTIO_MMIO_MAX_DEVICES, nameof(index));

        return s_virtioDevices[index] as T;
    }

    /// <summary>
    /// Retrieves a registered virtio device by its type.
    /// </summary>
    /// <typeparam name="T">The type of device to retrieve.</typeparam>
    /// <returns>The first device instance of the specified type, or null if not found.</returns>
    public static T? GetDevice<T>() where T : class
    {
        for (int i = 0; i < s_virtioDevices.Length; i++)
        {
            if (s_virtioDevices[i] is T device)
            {
                return device;
            }
        }
        return null;
    }

    public static IKeyboardDevice[] GetKeyboards()
    {
        // Count registered keyboards
        int count = 0;
        for (int i = 0; i < s_virtioDevices.Length; i++)
        {
            if (s_virtioDevices[i] is VirtioKeyboard)
            {
                count++;
            }
        }

        // Collect keyboards into an array
        var keyboards = new IKeyboardDevice[count];
        int index = 0;
        for (int i = 0; i < s_virtioDevices.Length; i++)
        {
            if (s_virtioDevices[i] is VirtioKeyboard keyboard)
            {
                keyboards[index++] = keyboard;
            }
        }

        return keyboards;
    }

    public static VirtioMouse[] GetMice()
    {
        // Count registered mice
        int count = 0;
        for (int i = 0; i < s_virtioDevices.Length; i++)
        {
            if (s_virtioDevices[i] is VirtioMouse)
            {
                count++;
            }
        }

        // Collect mice into an array
        var mice = new VirtioMouse[count];
        int index = 0;
        for (int i = 0; i < s_virtioDevices.Length; i++)
        {
            if (s_virtioDevices[i] is VirtioMouse mouse)
            {
                mice[index++] = mouse;
            }
        }

        return mice;
    }

    /// <summary>
    /// Initializes all available virtio MMIO devices on the system.
    /// This method scans for devices, performs the virtio initialization handshake,
    /// and registers supported device types (currently only input devices).
    /// Follows the virtio 1.1 specification for MMIO transport.
    /// </summary>
    /// <remarks>
    /// Assumes QEMU virt machine with virtio devices starting at 0x0a000000.
    /// Only input devices are currently supported for registration.
    /// </remarks>
    public static void InitializeDevices()
    {
        Serial.Write("[VirtioDevice] Scanning for virtio MMIO devices...\n");


        for (uint i = 0; i < VIRTIO_MMIO_MAX_DEVICES; i++)
        {
            ulong baseAddr = VIRTIO_MMIO_BASE + (i * VIRTIO_MMIO_SIZE);

            uint magic = Read32(baseAddr, REG_MAGIC);
            Serial.Write("[VirtioMMIO] Slot ");
            Serial.WriteNumber(i);
            Serial.Write(": base=0x");
            Serial.WriteHex(baseAddr);
            Serial.Write(", magic=0x");
            Serial.WriteHex(magic);

            if (magic != VIRTIO_MAGIC)
            {
                Serial.Write(" (not virtio)\n");
                continue;
            }

            uint version = Read32(baseAddr, REG_VERSION);
            uint deviceId = Read32(baseAddr, REG_DEVICE_ID);
            uint vendorId = Read32(baseAddr, REG_VENDOR_ID);
            uint irq = VIRTIO_IRQ_BASE + (uint)i;

            Serial.Write(", deviceId=");
            Serial.WriteNumber(deviceId);
            Serial.Write(", vendorId=0x");
            Serial.WriteHex(vendorId);
            Serial.Write(", version=");
            Serial.WriteNumber(version);

            if (deviceId == 0)
            {
                Serial.Write(" (empty)\n");
                continue;  // No device at this slot
            }

            Serial.Write(" (type: ");
            WriteDeviceTypeName(deviceId);
            Serial.Write(") IRQ=");
            Serial.WriteNumber(irq);
            Serial.Write("\n");


            switch (deviceId)
            {
                case VIRTIO_DEV_INPUT:
                    RegisterInputDevice(baseAddr, irq, version);
                    break;
                case VIRTIO_DEV_NET:
                    RegisterNetworkDevice(baseAddr, irq, version);
                    break;
                case VIRTIO_DEV_BLOCK:
                case VIRTIO_DEV_CONSOLE:
                case VIRTIO_DEV_RNG:
                case VIRTIO_DEV_GPU:
                    // Other device types not yet implemented
                    Serial.Write("[VirtioDevice] Skipping unsupported device type\n");
                    break;
                default:
                    Serial.Write("[VirtioDevice] Unknown device type\n");
                    break;
            }
        }
    }

    /// <summary>
    /// Registers a network device and initializes it.
    /// </summary>
    /// <param name="baseAddr">Base address of the virtio network device.</param>
    /// <param name="irq">IRQ number for the device.</param>
    /// <param name="version">Virtio device version.</param>
    private static void RegisterNetworkDevice(ulong baseAddr, uint irq, uint version)
    {
        if (CosmosFeatures.NetworkEnabled)
        {
            var netDevice = new VirtioNet(baseAddr, irq, version);
            uint index = irq - VIRTIO_IRQ_BASE;
            s_virtioDevices[index] = netDevice;
            netDevice.Initialize();
        }
    }

    /// <summary>
    /// Registers an input device based on enabled features.
    /// Creates either a mouse or keyboard device depending on CosmosFeatures and device capabilities.
    /// </summary>
    /// <param name="baseAddr">Base address of the virtio input device.</param>
    /// <param name="irq">IRQ number for the device.</param>
    /// <param name="version">Virtio device version.</param>
    private static void RegisterInputDevice(ulong baseAddr, uint irq, uint version)
    {
        uint index = irq - VIRTIO_IRQ_BASE;

        if (index >= VIRTIO_MMIO_MAX_DEVICES)
        {
            return;
        }

        // Do minimal virtio initialization to access config space
        Write32(baseAddr, REG_STATUS, 0);  // Reset
        Write32(baseAddr, REG_STATUS, STATUS_ACKNOWLEDGE);  // Acknowledge
        Write32(baseAddr, REG_STATUS, STATUS_ACKNOWLEDGE | STATUS_DRIVER);  // Set driver present

        // Probe supported event types for this device
        bool supportsKey = SupportsEventType(baseAddr, EV_KEY);
        bool supportsRel = SupportsEventType(baseAddr, EV_REL);
        // TODO: TouchScreen support with EV_ABS
        bool supportsAbs = SupportsEventType(baseAddr, EV_ABS);
        bool supportsSyn = SupportsEventType(baseAddr, EV_SYN);

        // Mouse devices support both EV_KEY and EV_REL.
        if (supportsKey && supportsRel)
        {
            if (CosmosFeatures.MouseEnabled)
            {
                var mouse = new VirtioMouse(baseAddr, irq, version);
                s_virtioDevices[index] = mouse;
                mouse.Initialize();
            }
            return;
        }

        // Keyboard devices support EV_KEY but not EV_REL.
        if (supportsKey)
        {
            if (CosmosFeatures.KeyboardEnabled)
            {
                var keyboard = new VirtioKeyboard(baseAddr, irq, version);
                s_virtioDevices[index] = keyboard;
                keyboard.Initialize();
            }
            return;
        }
    }

    /// <summary>
    /// Checks if the virtio input device supports a specific event type.
    /// </summary>
    /// <param name="baseAddr">Base address of the virtio device.</param>
    /// <param name="eventType">The event type to check (e.g., EV_KEY, EV_REL).</param>
    /// <returns>True if the event type is supported, false otherwise.</returns>
    private static bool SupportsEventType(ulong baseAddr, ushort eventType)
    {
        // Select event types in config space
        Write8(baseAddr, REG_CONFIG, 0x11); // select = 0x11 for event types
        Write8(baseAddr, REG_CONFIG + 1, (byte)eventType); // subsel = event type
        ushort size = Read16(baseAddr, REG_CONFIG + 2); // read size

        return size > 0;
    }

    /// <summary>
    /// Writes the human-readable name of a virtio device type to serial output.
    /// </summary>
    /// <param name="deviceId">The virtio device ID.</param>
    private static void WriteDeviceTypeName(uint deviceId)
    {
        VirtioMMIO.WriteDeviceTypeName(deviceId);
    }
}
