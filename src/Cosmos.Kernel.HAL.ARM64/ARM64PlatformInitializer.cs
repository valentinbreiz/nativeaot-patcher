// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.ARM64.Devices.Clock;
using Cosmos.Kernel.HAL.ARM64.Devices.Input;
using Cosmos.Kernel.HAL.ARM64.Devices.Network;
using Cosmos.Kernel.HAL.ARM64.Devices.Timer;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.HAL.Interfaces;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.ARM64;

/// <summary>
/// ARM64 platform initializer - creates ARM64-specific HAL components.
/// </summary>
public class ARM64PlatformInitializer : IPlatformInitializer
{
    private GenericTimer? _timer;

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

        // Initialize RTC (reads boot wall-clock time from PL031 if available)
        Serial.WriteString("[ARM64HAL] Initializing RTC...\n");
        new RTC().Initialize();

        if (CosmosFeatures.KeyboardEnabled || CosmosFeatures.MouseEnabled || CosmosFeatures.NetworkEnabled)
        {
            DeviceMapper.EnsureMapped(VirtioMMIO.VIRTIO_MMIO_BASE);
            // Scan for virtio devices
            Serial.WriteString("[ARM64HAL] Scanning for virtio devices...\n");
            VirtioDevice.InitializeDevices();
        }
    }

    public ITimerDevice CreateTimer()
    {
        if (!CosmosFeatures.TimerEnabled)
        {
            return null!;
        }

        if (_timer == null)
        {
            _timer = new GenericTimer();
            _timer.Initialize();
        }
        return _timer;
    }

    public IKeyboardDevice[] GetKeyboardDevices()
    {
        if (!CosmosFeatures.KeyboardEnabled)
        {
            return [];
        }

        return VirtioDevice.GetKeyboards();
    }

    public IMouseDevice[] GetMouseDevices()
    {
        if (!CosmosFeatures.MouseEnabled)
        {
            return [];
        }

        return VirtioDevice.GetMice();
    }

    public INetworkDevice? GetNetworkDevice()
    {
        return VirtioDevice.GetDevice<VirtioNet>();
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
