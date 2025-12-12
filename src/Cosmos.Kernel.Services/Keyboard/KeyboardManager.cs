// This code is licensed under MIT license (see LICENSE for details)
// Ported from Cosmos.System2/Keyboard/KeyboardManager.cs

using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.Services.Keyboard.ScanMaps;

namespace Cosmos.Kernel.Services.Keyboard;

/// <summary>
/// Manages keyboard input from physical keyboards.
/// </summary>
public static class KeyboardManager
{
    private static List<IKeyboardDevice>? _keyboards;
    private static Queue<KeyEvent>? _queuedKeys;
    private static ScanMapBase? _scanMap;
    private static bool _initialized;

    /// <summary>
    /// The num-lock state.
    /// </summary>
    public static bool NumLock { get; set; }

    /// <summary>
    /// The caps-lock state.
    /// </summary>
    public static bool CapsLock { get; set; }

    /// <summary>
    /// The scroll-lock state.
    /// </summary>
    public static bool ScrollLock { get; set; }

    /// <summary>
    /// Whether the Control (Ctrl) key is currently pressed.
    /// </summary>
    public static bool ControlPressed { get; set; }

    /// <summary>
    /// Whether the Shift key is currently pressed.
    /// </summary>
    public static bool ShiftPressed { get; set; }

    /// <summary>
    /// Whether the Alt key is currently pressed.
    /// </summary>
    public static bool AltPressed { get; set; }

    /// <summary>
    /// Whether a keyboard input is pending to be processed.
    /// </summary>
    public static bool KeyAvailable => _queuedKeys != null && _queuedKeys.Count > 0;

    /// <summary>
    /// Initializes the keyboard manager.
    /// Call RegisterKeyboard() after this to add keyboards.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _keyboards = new List<IKeyboardDevice>();
        _queuedKeys = new Queue<KeyEvent>();
        _scanMap = new USStandardLayout();

        _initialized = true;
    }

    /// <summary>
    /// Registers a keyboard device with the manager.
    /// </summary>
    public static void RegisterKeyboard(IKeyboardDevice keyboard)
    {
        if (_keyboards == null || keyboard == null)
            return;

        keyboard.OnKeyPressed = HandleScanCode;
        _keyboards.Add(keyboard);
    }

    /// <summary>
    /// Enqueues the given key-press event to the internal keyboard buffer.
    /// </summary>
    private static void Enqueue(KeyEvent keyEvent)
    {
        _queuedKeys?.Enqueue(keyEvent);
    }

    /// <summary>
    /// Handles a key-press by its physical key scan-code.
    /// </summary>
    public static void HandleScanCode(byte scanCode, bool released)
    {
        if (_scanMap == null)
            return;

        byte key = scanCode;

        if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.CapsLock) && !released)
        {
            CapsLock = !CapsLock;
            UpdateLeds();
        }
        else if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.NumLock) && !released)
        {
            NumLock = !NumLock;
            UpdateLeds();
        }
        else if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.ScrollLock) && !released)
        {
            ScrollLock = !ScrollLock;
            UpdateLeds();
        }
        else if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.LCtrl) || _scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.RCtrl))
        {
            ControlPressed = !released;
        }
        else if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.LShift) || _scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.RShift))
        {
            ShiftPressed = !released;
        }
        else if (_scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.LAlt) || _scanMap.ScanCodeMatchesKey(key, ConsoleKeyEx.RAlt))
        {
            AltPressed = !released;
        }
        else
        {
            if (!released)
            {
                if (GetKey(key, out var keyInfo))
                {
                    Enqueue(keyInfo!);
                }
            }
        }
    }

    /// <summary>
    /// Updates the keyboard LEDs.
    /// </summary>
    private static void UpdateLeds()
    {
        if (_keyboards == null)
            return;

        foreach (IKeyboardDevice keyboard in _keyboards)
        {
            keyboard.UpdateLeds();
        }
    }

    /// <summary>
    /// Attempts to convert the given physical key scan-code to a KeyEvent.
    /// </summary>
    public static bool GetKey(byte scanCode, out KeyEvent? keyInfo)
    {
        if (_scanMap == null)
        {
            keyInfo = null;
            return false;
        }
        keyInfo = _scanMap.ConvertScanCode(scanCode, ControlPressed, ShiftPressed, AltPressed, NumLock, CapsLock, ScrollLock);
        return keyInfo != null;
    }

    /// <summary>
    /// If available, reads the next key from the pending key-press buffer.
    /// </summary>
    public static bool TryReadKey(out KeyEvent? key)
    {
        if (_queuedKeys != null && _queuedKeys.Count > 0)
        {
            key = _queuedKeys.Dequeue();
            return true;
        }

        key = default;
        return false;
    }

    /// <summary>
    /// Reads the next key from the pending key-press buffer, blocking until available.
    /// </summary>
    public static KeyEvent ReadKey()
    {
        while (_queuedKeys == null || _queuedKeys.Count == 0)
        {
            // Halt CPU until interrupt (key press)
            HAL.PlatformHAL.CpuOps?.Halt();
        }

        return _queuedKeys.Dequeue();
    }

    /// <summary>
    /// Gets the currently used keyboard layout.
    /// </summary>
    public static ScanMapBase? GetKeyLayout() => _scanMap;

    /// <summary>
    /// Sets the currently used keyboard layout.
    /// </summary>
    public static void SetKeyLayout(ScanMapBase scanMap)
    {
        if (scanMap != null)
        {
            _scanMap = scanMap;
        }
    }

}
