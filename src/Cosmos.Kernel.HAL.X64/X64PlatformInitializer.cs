// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Power;
using Cosmos.Kernel.Core.X64;
using Cosmos.Kernel.Core.X64.Cpu;
using Cosmos.Kernel.Core.X64.IO;
using Cosmos.Kernel.Core.X64.Power;
using Cosmos.Kernel.HAL.Interfaces;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.X64.Devices.Clock;
using Cosmos.Kernel.HAL.X64.Devices.Input;
using Cosmos.Kernel.HAL.X64.Devices.Network;
using Cosmos.Kernel.HAL.X64.Devices.Timer;

namespace Cosmos.Kernel.HAL.X64;

/// <summary>
/// X64 platform initializer - creates x64-specific HAL components.
/// </summary>
public class X64PlatformInitializer : IPlatformInitializer
{
    private PIT? _pit;
    private RTC? _rtc;
    private PS2Controller? _ps2Controller;
    private E1000E? _networkDevice;

    public string PlatformName => "x86-64";
    public PlatformArchitecture Architecture => PlatformArchitecture.X64;

    public IPortIO CreatePortIO() => new X64PortIO();
    public ICpuOps CreateCpuOps() => new X64CpuOps();
    public IPowerOps CreatePowerOps() => new X64PowerOps();
    public IInterruptController CreateInterruptController() => new X64InterruptController();

    public void PreparePciMapping(ulong ecamBase)
    {
        // x64 uses legacy port I/O (0xCF8/0xCFC) for PCI config access,
        // which bypasses the MMU — no memory mapping needed.
    }

    public void InitializeHardware()
    {
        // Display ACPI MADT information
        Serial.WriteString("[X64HAL] Displaying ACPI MADT info...\n");
        AcpiMadt.DisplayMadtInfo();

        // Initialize APIC
        Serial.WriteString("[X64HAL] Initializing APIC...\n");
        ApicManager.Initialize();

        // Calibrate TSC frequency
        Serial.WriteString("[X64HAL] Calibrating TSC frequency...\n");
        X64CpuOps.CalibrateTsc();
        Serial.WriteString("[X64HAL] TSC frequency: ");
        Serial.WriteNumber((ulong)X64CpuOps.TscFrequency);
        Serial.WriteString(" Hz\n");

        // Initialize RTC
        Serial.WriteString("[X64HAL] Initializing RTC...\n");
        _rtc = new RTC();
        _rtc.Initialize();

        // Initialize PIT
        Serial.WriteString("[X64HAL] Initializing PIT...\n");
        _pit = new PIT();
        _pit.Initialize();
        _pit.RegisterIRQHandler();

        // Initialize PS/2 Controller (if keyboard or mouse feature enabled)
        if (CosmosFeatures.KeyboardEnabled || CosmosFeatures.MouseEnabled)
        {
            Serial.WriteString("[X64HAL] Initializing PS/2 controller...\n");
            _ps2Controller = new PS2Controller();
            _ps2Controller.Initialize();
        }

        // Try to find E1000E network device (if network feature enabled)
        if (CosmosFeatures.NetworkEnabled)
        {
            Serial.WriteString("[X64HAL] Looking for E1000E network device...\n");
            _networkDevice = E1000E.FindAndCreate();
            if (_networkDevice != null)
            {
                Serial.WriteString("[X64HAL] E1000E device found, initializing...\n");
                _networkDevice.Initialize();
                _networkDevice.RegisterIRQHandler();
            }
            else
            {
                Serial.WriteString("[X64HAL] No E1000E device found\n");
            }
        }
    }

    public ITimerDevice CreateTimer()
    {
        if (!CosmosFeatures.TimerEnabled)
        {
            return null!;
        }

        if (_pit == null)
        {
            _pit = new PIT();
            _pit.Initialize();
        }
        return _pit;
    }

    public IKeyboardDevice[] GetKeyboardDevices()
    {
        if (!CosmosFeatures.KeyboardEnabled || _ps2Controller == null)
        {
            return [];
        }

        return PS2Controller.GetKeyboardDevices();
    }

    public IMouseDevice[] GetMouseDevices()
    {
        if (!CosmosFeatures.MouseEnabled || _ps2Controller == null)
        {
            return [];
        }

        return PS2Controller.GetMouseDevices();
    }

    public INetworkDevice? GetNetworkDevice()
    {
        return _networkDevice;
    }

    public unsafe uint GetCpuCount()
    {
        var madtInfo = AcpiMadt.GetMadtInfoPtr();
        return madtInfo != null ? madtInfo->CpuCount : 1;
    }

    public void StartSchedulerTimer(uint quantumMs)
    {
        // Register LAPIC timer handler
        Serial.WriteString("[X64HAL] Registering LAPIC timer handler...\n");
        LocalApic.RegisterTimerHandler();

        // Start LAPIC timer for preemptive scheduling
        Serial.WriteString("[X64HAL] Starting LAPIC timer for scheduling...\n");
        LocalApic.StartPeriodicTimer(quantumMs);
    }
}
