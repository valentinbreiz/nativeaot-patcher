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
}
