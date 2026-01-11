// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Input;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Input;

/// <summary>
/// Virtio-input keyboard driver for ARM64.
/// Provides keyboard input via virtio-input device on QEMU virt machine.
/// </summary>
public unsafe class VirtioKeyboard : KeyboardDevice
{
    // Linux input event types
    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;
    private const ushort EV_ABS = 0x03;

    // Event queue index
    private const int EVENTQ = 0;
    private const int STATUSQ = 1;

    // Queue size
    private const uint QUEUE_SIZE = 64;

    private readonly ulong _baseAddress;
    private readonly uint _irq;
    private readonly uint _mmioVersion;
    private Virtqueue? _eventQueue;

    // Event buffers
    private VirtioInputEvent* _eventBuffers;
    private const int NUM_EVENT_BUFFERS = 32;

    private bool _initialized;

    public static VirtioKeyboard? Instance { get; private set; }

    // Static callback for key events (set by KeyboardManager) - mirrors x64 PS2Keyboard
    // Note: OnKeyPressed (from base class) is set by KeyboardManager.RegisterKeyboard()

    /// <summary>
    /// Returns true if the device was successfully initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    public override bool KeyAvailable => false;  // Events are pushed via interrupt

    private VirtioKeyboard(ulong baseAddress, uint irq, uint mmioVersion)
    {
        _baseAddress = baseAddress;
        _irq = irq;
        _mmioVersion = mmioVersion;
    }

    /// <summary>
    /// Finds and creates a virtio keyboard device.
    /// </summary>
    public static VirtioKeyboard? FindAndCreate()
    {
        Serial.Write("[VirtioKeyboard] Searching for virtio-input device...\n");

        if (!VirtioMMIO.FindDevice(VirtioMMIO.VIRTIO_DEV_INPUT, out ulong baseAddr, out uint irq))
        {
            Serial.Write("[VirtioKeyboard] No virtio-input device found\n");
            return null;
        }

        // Read MMIO version (1 = legacy, 2 = modern)
        uint version = VirtioMMIO.Read32(baseAddr, VirtioMMIO.REG_VERSION);

        Serial.Write("[VirtioKeyboard] Found virtio-input at 0x");
        Serial.WriteHex(baseAddr);
        Serial.Write(" IRQ=");
        Serial.WriteNumber(irq);
        Serial.Write(" MMIO version=");
        Serial.WriteNumber(version);
        Serial.Write("\n");

        var keyboard = new VirtioKeyboard(baseAddr, irq, version);
        Instance = keyboard;
        return keyboard;
    }

    /// <summary>
    /// Initializes the virtio keyboard device.
    /// </summary>
    public override void Initialize()
    {
        Serial.Write("[VirtioKeyboard] Initializing (MMIO version ");
        Serial.WriteNumber(_mmioVersion);
        Serial.Write(")...\n");

        // Reset device
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, 0);

        // For legacy MMIO (version 1), set guest page size before any queue setup
        if (_mmioVersion == 1)
        {
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_GUEST_PAGE_SIZE, 4096);
            Serial.Write("[VirtioKeyboard] Set guest page size to 4096\n");
        }

        // Set ACKNOWLEDGE status bit
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_ACKNOWLEDGE);

        // Set DRIVER status bit
        uint status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_DRIVER);

        // Read and negotiate features (we don't need any special features)
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DEVICE_FEATURES_SEL, 0);
        uint features = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_DEVICE_FEATURES);
        Serial.Write("[VirtioKeyboard] Device features: 0x");
        Serial.WriteHex(features);
        Serial.Write("\n");

        // Accept no features
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES_SEL, 0);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES, 0);

        // For modern MMIO (version 2), need to set FEATURES_OK
        if (_mmioVersion >= 2)
        {
            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_FEATURES_OK);

            // Check FEATURES_OK was set
            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            if ((status & VirtioMMIO.STATUS_FEATURES_OK) == 0)
            {
                Serial.Write("[VirtioKeyboard] ERROR: Device did not accept features\n");
                VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
                return;
            }
        }

        // Set up event queue
        if (!SetupQueue(EVENTQ))
        {
            Serial.Write("[VirtioKeyboard] ERROR: Failed to setup event queue\n");
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
            return;
        }

        // Allocate event buffers and add to queue
        _eventBuffers = (VirtioInputEvent*)MemoryOp.Alloc((uint)(NUM_EVENT_BUFFERS * sizeof(VirtioInputEvent)));
        for (int i = 0; i < NUM_EVENT_BUFFERS; i++)
        {
            AddEventBuffer(i);
        }

        // Notify device that buffers are available
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, EVENTQ);

        // Set DRIVER_OK to complete initialization
        status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_DRIVER_OK);

        _initialized = true;
        Serial.Write("[VirtioKeyboard] Initialization complete\n");
    }

    private bool SetupQueue(int queueIndex)
    {
        // Select queue
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_SEL, (uint)queueIndex);

        // For legacy (version 1), check PFN; for modern (version 2), check READY
        if (_mmioVersion == 1)
        {
            uint pfn = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN);
            if (pfn != 0)
            {
                Serial.Write("[VirtioKeyboard] Queue ");
                Serial.WriteNumber((uint)queueIndex);
                Serial.Write(" already in use (PFN=");
                Serial.WriteNumber(pfn);
                Serial.Write(")\n");
                return false;
            }
        }
        else
        {
            uint ready = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_READY);
            if (ready != 0)
            {
                Serial.Write("[VirtioKeyboard] Queue ");
                Serial.WriteNumber((uint)queueIndex);
                Serial.Write(" already in use\n");
                return false;
            }
        }

        // Get max queue size
        uint maxSize = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM_MAX);
        if (maxSize == 0)
        {
            Serial.Write("[VirtioKeyboard] Queue ");
            Serial.WriteNumber((uint)queueIndex);
            Serial.Write(" not available\n");
            return false;
        }

        uint queueSize = maxSize < QUEUE_SIZE ? maxSize : QUEUE_SIZE;

        Serial.Write("[VirtioKeyboard] Setting up queue ");
        Serial.WriteNumber((uint)queueIndex);
        Serial.Write(" size=");
        Serial.WriteNumber(queueSize);
        Serial.Write("\n");

        // Create virtqueue
        _eventQueue = new Virtqueue(queueSize);

        // Set queue size
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM, queueSize);

        if (_mmioVersion == 1)
        {
            // Legacy: set queue alignment and PFN (page frame number)
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_ALIGN, 4096);

            ulong baseAddr = _eventQueue.QueueBaseAddr;
            uint pfn = (uint)(baseAddr / 4096);

            Serial.Write("[VirtioKeyboard] Queue PFN=0x");
            Serial.WriteHex(pfn);
            Serial.Write(" (addr=0x");
            Serial.WriteHex(baseAddr);
            Serial.Write(")\n");

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN, pfn);
        }
        else
        {
            // Modern: set queue addresses (split into low/high for 64-bit)
            ulong descAddr = _eventQueue.DescriptorTableAddr;
            ulong availAddr = _eventQueue.AvailableRingAddr;
            ulong usedAddr = _eventQueue.UsedRingAddr;

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_LOW, (uint)descAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_HIGH, (uint)(descAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_LOW, (uint)availAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_HIGH, (uint)(availAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_LOW, (uint)usedAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_HIGH, (uint)(usedAddr >> 32));

            // Enable queue (modern only uses READY register)
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_READY, 1);
        }

        return true;
    }

    private void AddEventBuffer(int bufferIndex)
    {
        if (_eventQueue == null)
            return;

        int descIdx = _eventQueue.AllocDescriptor();
        if (descIdx < 0)
            return;

        ulong bufferAddr = (ulong)(&_eventBuffers[bufferIndex]);
        _eventQueue.SetupDescriptor(descIdx, bufferAddr, (uint)sizeof(VirtioInputEvent),
            Virtqueue.VRING_DESC_F_WRITE, 0);
        _eventQueue.AddAvailable((ushort)descIdx);
    }

    /// <summary>
    /// Registers the IRQ handler for keyboard interrupts.
    /// </summary>
    public void RegisterIRQHandler()
    {
        Serial.Write("[VirtioKeyboard] Registering IRQ handler for INTID ");
        Serial.WriteNumber(_irq);
        Serial.Write("\n");

        // Configure interrupt as edge-triggered (virtio uses MSI-style signaling)
        GIC.ConfigureInterrupt(_irq, true);

        // Enable interrupt in GIC
        GIC.SetPriority(_irq, 0x80);
        GIC.EnableInterrupt(_irq);

        // Register handler
        InterruptManager.SetHandler((byte)_irq, HandleIRQ);

        Serial.Write("[VirtioKeyboard] IRQ handler registered\n");
    }

    private static void HandleIRQ(ref IRQContext ctx)
    {
        if (Instance == null || !Instance._initialized)
            return;

        // Read and acknowledge interrupt
        uint intStatus = VirtioMMIO.Read32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);
        VirtioMMIO.Write32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_ACK, intStatus);

        Serial.Write("[VirtioKeyboard] IRQ! status=0x");
        Serial.WriteHex(intStatus);
        Serial.Write("\n");

        // Process used buffers
        Instance.ProcessEvents();
    }

    private void ProcessEvents()
    {
        if (_eventQueue == null)
            return;

        bool hasBuffers = _eventQueue.HasUsedBuffers();
        if (!hasBuffers)
        {
            Serial.Write("[VirtioKeyboard] No used buffers\n");
        }

        while (_eventQueue.HasUsedBuffers())
        {
            if (!_eventQueue.GetUsedBuffer(out uint id, out uint len))
                break;

            // Process the event
            VirtioInputEvent* evt = &_eventBuffers[id];

            if (evt->Type == EV_KEY)
            {
                // Convert Linux keycode to PS2 scan code
                byte scanCode = LinuxToPS2ScanCode(evt->Code);
                bool released = evt->Value == 0;

                Serial.Write("[VirtioKeyboard] Key ");
                Serial.WriteNumber(evt->Code);
                Serial.Write(" -> scan 0x");
                Serial.WriteHex(scanCode);
                Serial.Write(released ? " released\n" : " pressed\n");

                // Invoke instance callback (set by KeyboardManager.RegisterKeyboard)
                Instance?.OnKeyPressed?.Invoke(scanCode, released);
            }

            // Re-add buffer to queue
            _eventQueue.FreeDescriptor((int)id);
            AddEventBuffer((int)id);

            // Notify device
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, EVENTQ);
        }
    }

    private static uint _pollCount = 0;
    private static bool _pollFirstCall = false;

    /// <summary>
    /// Polls for keyboard events (for non-interrupt mode).
    /// </summary>
    public override void Poll()
    {
        if (!_pollFirstCall)
        {
            _pollFirstCall = true;
            Serial.Write("[VirtioKeyboard] Poll() first call\n");
        }

        if (!_initialized || _eventQueue == null)
            return;

        _pollCount++;

        // Log every 1000 polls to show we're polling
        if (_pollCount % 1000 == 0)
        {
            uint status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            uint intStatus = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);
            Serial.Write("[VirtioKeyboard] Poll #");
            Serial.WriteNumber(_pollCount);
            Serial.Write(" status=0x");
            Serial.WriteHex(status);
            Serial.Write(" intStatus=0x");
            Serial.WriteHex(intStatus);
            Serial.Write("\n");
        }

        // Check interrupt status register
        uint intStat = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);

        // Check for used buffers
        bool hasBuffers = _eventQueue.HasUsedBuffers();

        if (intStat != 0 || hasBuffers)
        {
            Serial.Write("[VirtioKeyboard] Event! intStatus=0x");
            Serial.WriteHex(intStat);
            Serial.Write(" hasBuffers=");
            Serial.Write(hasBuffers ? "true" : "false");
            Serial.Write("\n");

            if (intStat != 0)
            {
                VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_INTERRUPT_ACK, intStat);
            }

            if (hasBuffers)
            {
                ProcessEvents();
            }
        }
    }

    /// <summary>
    /// Static poll for use from timer or main loop.
    /// </summary>
    public static void PollStatic()
    {
        Instance?.Poll();
    }

    /// <summary>
    /// Checks if GIC interrupt is pending.
    /// </summary>
    public void DebugGicState()
    {
        bool pending = GIC.IsInterruptPending(_irq);
        Serial.Write("[VirtioKeyboard] GIC IRQ ");
        Serial.WriteNumber(_irq);
        Serial.Write(" pending=");
        Serial.Write(pending ? "true" : "false");
        Serial.Write("\n");
    }

    public override void UpdateLeds()
    {
        // LED updates via status queue not implemented yet
    }

    public override void Enable()
    {
        // Already enabled after Initialize
    }

    public override void Disable()
    {
        // Not implemented - would need to disable IRQ
    }

    /// <summary>
    /// Converts Linux keycode to PS/2 scan code set 1.
    /// </summary>
    private static byte LinuxToPS2ScanCode(ushort linuxCode)
    {
        // Linux keycodes are mostly similar to PS/2 scan codes for basic keys
        // This is a simplified mapping for common keys
        return linuxCode switch
        {
            // Function row
            1 => 0x01,   // KEY_ESC -> Esc
            59 => 0x3B,  // KEY_F1 -> F1
            60 => 0x3C,  // KEY_F2 -> F2
            61 => 0x3D,  // KEY_F3 -> F3
            62 => 0x3E,  // KEY_F4 -> F4
            63 => 0x3F,  // KEY_F5 -> F5
            64 => 0x40,  // KEY_F6 -> F6
            65 => 0x41,  // KEY_F7 -> F7
            66 => 0x42,  // KEY_F8 -> F8
            67 => 0x43,  // KEY_F9 -> F9
            68 => 0x44,  // KEY_F10 -> F10

            // Number row
            2 => 0x02,   // KEY_1 -> 1
            3 => 0x03,   // KEY_2 -> 2
            4 => 0x04,   // KEY_3 -> 3
            5 => 0x05,   // KEY_4 -> 4
            6 => 0x06,   // KEY_5 -> 5
            7 => 0x07,   // KEY_6 -> 6
            8 => 0x08,   // KEY_7 -> 7
            9 => 0x09,   // KEY_8 -> 8
            10 => 0x0A,  // KEY_9 -> 9
            11 => 0x0B,  // KEY_0 -> 0
            12 => 0x0C,  // KEY_MINUS -> -
            13 => 0x0D,  // KEY_EQUAL -> =
            14 => 0x0E,  // KEY_BACKSPACE -> Backspace

            // Top letter row
            15 => 0x0F,  // KEY_TAB -> Tab
            16 => 0x10,  // KEY_Q -> Q
            17 => 0x11,  // KEY_W -> W
            18 => 0x12,  // KEY_E -> E
            19 => 0x13,  // KEY_R -> R
            20 => 0x14,  // KEY_T -> T
            21 => 0x15,  // KEY_Y -> Y
            22 => 0x16,  // KEY_U -> U
            23 => 0x17,  // KEY_I -> I
            24 => 0x18,  // KEY_O -> O
            25 => 0x19,  // KEY_P -> P
            26 => 0x1A,  // KEY_LEFTBRACE -> [
            27 => 0x1B,  // KEY_RIGHTBRACE -> ]
            28 => 0x1C,  // KEY_ENTER -> Enter

            // Middle letter row
            29 => 0x1D,  // KEY_LEFTCTRL -> Left Ctrl
            30 => 0x1E,  // KEY_A -> A
            31 => 0x1F,  // KEY_S -> S
            32 => 0x20,  // KEY_D -> D
            33 => 0x21,  // KEY_F -> F
            34 => 0x22,  // KEY_G -> G
            35 => 0x23,  // KEY_H -> H
            36 => 0x24,  // KEY_J -> J
            37 => 0x25,  // KEY_K -> K
            38 => 0x26,  // KEY_L -> L
            39 => 0x27,  // KEY_SEMICOLON -> ;
            40 => 0x28,  // KEY_APOSTROPHE -> '
            41 => 0x29,  // KEY_GRAVE -> `

            // Bottom letter row
            42 => 0x2A,  // KEY_LEFTSHIFT -> Left Shift
            43 => 0x2B,  // KEY_BACKSLASH -> \
            44 => 0x2C,  // KEY_Z -> Z
            45 => 0x2D,  // KEY_X -> X
            46 => 0x2E,  // KEY_C -> C
            47 => 0x2F,  // KEY_V -> V
            48 => 0x30,  // KEY_B -> B
            49 => 0x31,  // KEY_N -> N
            50 => 0x32,  // KEY_M -> M
            51 => 0x33,  // KEY_COMMA -> ,
            52 => 0x34,  // KEY_DOT -> .
            53 => 0x35,  // KEY_SLASH -> /
            54 => 0x36,  // KEY_RIGHTSHIFT -> Right Shift

            // Bottom row
            56 => 0x38,  // KEY_LEFTALT -> Left Alt
            57 => 0x39,  // KEY_SPACE -> Space
            58 => 0x3A,  // KEY_CAPSLOCK -> Caps Lock

            // Arrow keys (extended)
            103 => 0x48, // KEY_UP -> Up
            105 => 0x4B, // KEY_LEFT -> Left
            106 => 0x4D, // KEY_RIGHT -> Right
            108 => 0x50, // KEY_DOWN -> Down

            _ => 0x00    // Unknown key
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VirtioInputEvent
    {
        public ushort Type;
        public ushort Code;
        public uint Value;
    }
}
