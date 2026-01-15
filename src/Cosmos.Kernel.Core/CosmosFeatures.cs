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
}
