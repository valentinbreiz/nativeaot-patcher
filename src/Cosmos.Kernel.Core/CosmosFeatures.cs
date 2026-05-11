using System.Diagnostics.CodeAnalysis;

namespace Cosmos.Kernel.Core;

/// <summary>
/// Centralized feature flags for Cosmos kernel components.
/// These flags can be set via RuntimeHostConfigurationOption in csproj
/// and are used by ILC for trimming.
/// </summary>
public static class CosmosFeatures
{
    /// <summary>
    /// Controls interrupt setup (IDT/IRQ). Disabling this also disables Timer, Keyboard,
    /// Mouse, Network, Scheduler, and Graphics via the MSBuild cascade in Sdk.targets.
    /// Set via CosmosEnableInterrupts property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.HAL.Interrupts.Enabled")]
    public static bool InterruptsEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.HAL.Interrupts.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls UART support initialization.
    /// Set via CosmosEnableUART property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.HAL.UART.Enabled")]
    public static bool UARTEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.HAL.UART.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls PCI support initialization.
    /// Set via CosmosEnablePCI property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.HAL.PCI.Enabled")]
    public static bool PCIEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.HAL.PCI.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls timer (PIT/HPET) initialization. Disabling this also disables Scheduler.
    /// Set via CosmosEnableTimer property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.System.Timer.Enabled")]
    public static bool TimerEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.System.Timer.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls keyboard support initialization.
    /// Set via CosmosEnableKeyboard property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.System.Input.Keyboard.Enabled")]
    public static bool KeyboardEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.System.Input.Keyboard.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls network support initialization.
    /// Set via CosmosEnableNetwork property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.System.Network.Enabled")]
    public static bool NetworkEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.System.Network.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls scheduler/threading support initialization.
    /// Set via CosmosEnableScheduler property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.Core.Scheduler.Enabled")]
    public static bool SchedulerEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.Core.Scheduler.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls graphics support initialization.
    /// Set via CosmosEnableGraphics property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.System.Graphics.Enabled")]
    public static bool GraphicsEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.System.Graphics.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Controls mouse support initialization.
    /// Set via CosmosEnableMouse property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.System.Input.Mouse.Enabled")]
    public static bool MouseEnabled =>
        AppContext.TryGetSwitch("Cosmos.Kernel.System.Input.Mouse.Enabled", out bool enabled) ? enabled : true;

    /// <summary>
    /// Selects the GC stack-scanning strategy. When true (default), the mark phase
    /// walks every word of each thread's stack and treats anything that looks like
    /// a heap pointer as a root. When false, the kernel expects a precise scanner
    /// backed by NativeAOT GCInfo to be wired up — see issue #346. Today flipping
    /// this off panics at first collection because the precise path is not landed.
    /// Set via CosmosEnableConservativeGCStackScan property in csproj.
    /// </summary>
    [FeatureSwitchDefinition("Cosmos.Kernel.Memory.GC.ConservativeStackScan")]
    public static bool ConservativeStackScan =>
        AppContext.TryGetSwitch("Cosmos.Kernel.Memory.GC.ConservativeStackScan", out bool enabled) ? enabled : true;
}
