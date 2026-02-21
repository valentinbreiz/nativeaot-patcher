// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.X64.Cpu;
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
    public IInterruptController CreateInterruptController() => new X64InterruptController();

    public void InitializeHardware()
    {
        // Display ACPI MADT information
        Serial.WriteString("[X64HAL] Displaying ACPI MADT info...\n");
        Acpi.Acpi.DisplayMadtInfo();

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

        // Initialize PS/2 Controller (if keyboard feature enabled)
        if (CosmosFeatures.KeyboardEnabled)
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
                _networkDevice.InitializeNetwork();
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
        if (_pit == null)
        {
            _pit = new PIT();
            _pit.Initialize();
        }
        return _pit;
    }

    public IKeyboardDevice[] GetKeyboardDevices()
    {
        if (_ps2Controller == null)
        {
            return [];
        }
        return PS2Controller.GetKeyboardDevices();
    }

    public INetworkDevice? GetNetworkDevice()
    {
        return _networkDevice;
    }

    public unsafe uint GetCpuCount()
    {
        var madtInfo = Acpi.Acpi.GetMadtInfoPtr();
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
