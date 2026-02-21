// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.ARM64.Devices.Input;
using Cosmos.Kernel.HAL.ARM64.Devices.Timer;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.HAL.ARM64.Devices.Network;
using Cosmos.Kernel.HAL.Interfaces;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.ARM64;

/// <summary>
/// ARM64 platform initializer - creates ARM64-specific HAL components.
/// </summary>
public class ARM64PlatformInitializer : IPlatformInitializer
{
    private GenericTimer? _timer;
    private VirtioKeyboard? _virtioKeyboard;
    private VirtioNet? _networkDevice;

    public string PlatformName => "ARM64";
    public PlatformArchitecture Architecture => PlatformArchitecture.ARM64;

    public IPortIO CreatePortIO() => new ARM64MemoryIO();
    public ICpuOps CreateCpuOps() => new ARM64CpuOps();
    public IInterruptController CreateInterruptController() => new ARM64InterruptController();

    public void InitializeHardware()
    {
        // Initialize Generic Timer
        Serial.WriteString("[ARM64HAL] Initializing Generic Timer...\n");
        _timer = new GenericTimer();
        _timer.Initialize();

        // Register timer interrupt handler
        Serial.WriteString("[ARM64HAL] Registering timer interrupt handler...\n");
        _timer.RegisterIRQHandler();

        // Scan for virtio devices
        Serial.WriteString("[ARM64HAL] Scanning for virtio devices...\n");
        VirtioMMIO.ScanDevices();

        // Initialize virtio keyboard
        _virtioKeyboard = VirtioKeyboard.FindAndCreate();
        if (_virtioKeyboard != null)
        {
            _virtioKeyboard.Initialize();
            if (_virtioKeyboard.IsInitialized)
            {
                Serial.WriteString("[ARM64HAL] Virtio keyboard initialized\n");
            }
            else
            {
                Serial.WriteString("[ARM64HAL] Virtio keyboard initialization failed\n");
                _virtioKeyboard = null;
            }
        }
        else
        {
            Serial.WriteString("[ARM64HAL] No virtio keyboard found\n");
        }

        // Try to find VirtioNet MMIO network device (if network feature enabled)
        if (CosmosFeatures.NetworkEnabled)
        {
            Serial.WriteString("[ARM64HAL] Looking for VirtioNet MMIO network device...\n");
            _networkDevice = VirtioNet.FindAndCreate();
            if (_networkDevice != null)
            {
                Serial.WriteString("[ARM64HAL] VirtioNet MMIO device found, initializing...\n");
                _networkDevice.Initialize();
            }
            else
            {
                Serial.WriteString("[ARM64HAL] No VirtioNet MMIO device found\n");
            }
        }
    }

    public ITimerDevice CreateTimer()
    {
        if (_timer == null)
        {
            _timer = new GenericTimer();
            _timer.Initialize();
        }
        return _timer;
    }

    public IKeyboardDevice[] GetKeyboardDevices()
    {
        if (_virtioKeyboard != null && _virtioKeyboard.IsInitialized)
        {
            return [_virtioKeyboard];
        }
        return [];
    }

    public INetworkDevice? GetNetworkDevice()
    {
        return _networkDevice;
    }

    public uint GetCpuCount()
    {
        // For now, single CPU on ARM64
        return 1;
    }

    public void StartSchedulerTimer(uint quantumMs)
    {
        // Start the timer for preemptive scheduling
        Serial.WriteString("[ARM64HAL] Starting Generic Timer for scheduling...\n");
        _timer?.Start();
    }
}
