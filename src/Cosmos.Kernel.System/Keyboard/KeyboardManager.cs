// This code is licensed under MIT license (see LICENSE for details)
// Ported from Cosmos.System2/Keyboard/KeyboardManager.cs

using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Keyboard.ScanMaps;

namespace Cosmos.Kernel.System.Keyboard;

/// <summary>
/// Manages keyboard input from physical keyboards.
/// </summary>
public static class KeyboardManager
{
    /// <summary>
    /// Whether keyboard support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.KeyboardEnabled;

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

        // Enable keyboard after callback is set (this registers IRQ handler)
        keyboard.Enable();

        Cosmos.Kernel.Core.IO.Serial.Write("[KeyboardManager] Registered keyboard, total: ");
        Cosmos.Kernel.Core.IO.Serial.WriteNumber((uint)_keyboards.Count);
        Cosmos.Kernel.Core.IO.Serial.Write("\n");
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

    private static bool _readKeyEntered = false;

    /// <summary>
    /// Reads the next key from the pending key-press buffer, blocking until available.
    /// </summary>
    public static KeyEvent ReadKey()
    {
        if (!_readKeyEntered)
        {
            _readKeyEntered = true;
            Cosmos.Kernel.Core.IO.Serial.Write("[KeyboardManager] ReadKey() entered\n");
        }

        while (_queuedKeys == null || _queuedKeys.Count == 0)
        {
            // Poll all keyboards for events (in case interrupts aren't working)
            PollKeyboards();

            // Halt CPU until interrupt (key press)
            HAL.PlatformHAL.CpuOps?.Halt();
        }

        return _queuedKeys.Dequeue();
    }

    private static uint _pollCallCount = 0;

    private static bool _pollEntered = false;

    /// <summary>
    /// Polls all registered keyboards for events.
    /// </summary>
    private static void PollKeyboards()
    {
        if (!_pollEntered)
        {
            _pollEntered = true;
            Cosmos.Kernel.Core.IO.Serial.Write("[KeyboardManager] PollKeyboards() first call\n");
        }

        if (_keyboards == null)
            return;

        _pollCallCount++;
        if (_pollCallCount % 100 == 0)
        {
            Cosmos.Kernel.Core.IO.Serial.Write("[KeyboardManager] PollKeyboards #");
            Cosmos.Kernel.Core.IO.Serial.WriteNumber(_pollCallCount);
            Cosmos.Kernel.Core.IO.Serial.Write(" keyboards=");
            Cosmos.Kernel.Core.IO.Serial.WriteNumber((uint)_keyboards.Count);
            Cosmos.Kernel.Core.IO.Serial.Write("\n");
        }

        foreach (var keyboard in _keyboards)
        {
            keyboard.Poll();
        }
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
