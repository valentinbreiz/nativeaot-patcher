// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.Devices.Input;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Mouse;

/// <summary>
/// Manages mouse input from physical mouse devices.
/// </summary>
public static class MouseManager
{
    /// <summary>
    /// Whether mouse support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.MouseEnabled;

    private static List<IMouseDevice>? _mice;
    private static bool _initialized;

    /// <summary>
    /// Current X position (screen coordinates).
    /// </summary>
    public static int X { get; private set; }

    /// <summary>
    /// Current Y position (screen coordinates).
    /// </summary>
    public static int Y { get; private set; }

    /// <summary>
    /// Left button state.
    /// </summary>
    public static bool LeftButton { get; private set; }

    /// <summary>
    /// Right button state.
    /// </summary>
    public static bool RightButton { get; private set; }

    /// <summary>
    /// Middle button state.
    /// </summary>
    public static bool MiddleButton { get; private set; }

    /// <summary>
    /// Screen width for boundary checking.
    /// </summary>
    public static int ScreenWidth { get; set; } = 1024;

    /// <summary>
    /// Screen height for boundary checking.
    /// </summary>
    public static int ScreenHeight { get; set; } = 768;

    /// <summary>
    /// Mouse sensitivity multiplier (default 1.0).
    /// </summary>
    public static float Sensitivity { get; set; } = 1.0f;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Mouse support is disabled. Set CosmosEnableMouse=true in your csproj to enable it.");
    }

    /// <summary>
    /// Initializes the mouse manager.
    /// Call RegisterMouse() after this to add mice.
    /// </summary>
    public static void Initialize()
    {
        ThrowIfDisabled();

        if (_initialized)
            return;

        _mice = new List<IMouseDevice>();
        X = ScreenWidth / 2;
        Y = ScreenHeight / 2;

        _initialized = true;
    }

    /// <summary>
    /// Registers a mouse device with the manager.
    /// </summary>
    public static void RegisterMouse(IMouseDevice mouse)
    {
        if (_mice == null || mouse == null)
            return;

        // Set up event handler for mouse devices that use MouseDevice base class
        if (mouse is MouseDevice mouseDevice)
        {
            mouseDevice.OnMouseEvent = HandleMouseEvent;
        }

        _mice.Add(mouse);

        // Enable mouse after callback is set
        mouse.Enable();

        Cosmos.Kernel.Core.IO.Serial.Write("[MouseManager] Registered mouse, total: ");
        Cosmos.Kernel.Core.IO.Serial.WriteNumber((uint)_mice.Count);
        Cosmos.Kernel.Core.IO.Serial.Write("\n");
    }

    /// <summary>
    /// Handles mouse events from devices.
    /// </summary>
    private static void HandleMouseEvent(int deltaX, int deltaY, bool leftButton, bool rightButton, bool middleButton)
    {
        // Apply sensitivity
        int adjustedDeltaX = (int)(deltaX * Sensitivity);
        int adjustedDeltaY = (int)(deltaY * Sensitivity);

        // Update position with boundary checking
        X += adjustedDeltaX;
        Y += adjustedDeltaY;

        // Clamp to screen bounds
        if (X < 0) X = 0;
        if (X >= ScreenWidth) X = ScreenWidth - 1;
        if (Y < 0) Y = 0;
        if (Y >= ScreenHeight) Y = ScreenHeight - 1;

        // Update button states
        LeftButton = leftButton;
        RightButton = rightButton;
        MiddleButton = middleButton;
    }

    /// <summary>
    /// Polls all registered mice for events.
    /// </summary>
    public static void Poll()
    {
        if (_mice == null)
            return;

        foreach (var mouse in _mice)
        {
            mouse.Poll();
        }
    }

    /// <summary>
    /// Sets the mouse position directly (useful for initialization or reset).
    /// </summary>
    public static void SetPosition(int x, int y)
    {
        ThrowIfDisabled();

        X = x;
        Y = y;

        // Clamp to screen bounds
        if (X < 0) X = 0;
        if (X >= ScreenWidth) X = ScreenWidth - 1;
        if (Y < 0) Y = 0;
        if (Y >= ScreenHeight) Y = ScreenHeight - 1;
    }

    /// <summary>
    /// Updates screen dimensions (call when resolution changes).
    /// </summary>
    public static void SetScreenSize(int width, int height)
    {
        ThrowIfDisabled();

        ScreenWidth = width;
        ScreenHeight = height;

        // Ensure cursor is still within bounds
        if (X >= ScreenWidth) X = ScreenWidth - 1;
        if (Y >= ScreenHeight) Y = ScreenHeight - 1;
    }
}
