// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Input;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Input;

/// <summary>
/// Virtio-input mouse driver for ARM64.
/// Provides mouse input via virtio-input device on QEMU virt machine.
/// </summary>
public unsafe class VirtioMouse : MouseDevice
{
    // Linux input event types
    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;

    // Linux mouse button codes
    private const ushort BTN_LEFT = 0x110;
    private const ushort BTN_RIGHT = 0x111;
    private const ushort BTN_MIDDLE = 0x112;

    // Linux relative axis codes
    private const ushort REL_X = 0x00;
    private const ushort REL_Y = 0x01;
    private const ushort REL_WHEEL = 0x08;

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
    private bool _irqRegistered;

    // Temporary state for accumulating events
    private int _tempDeltaX;
    private int _tempDeltaY;
    private bool _tempLeftButton;
    private bool _tempRightButton;
    private bool _tempMiddleButton;
    private bool _hasEvents;

    public static VirtioMouse? Instance { get; private set; }

    public override bool DataAvailable => false;  // Events are pushed via interrupt

    private VirtioMouse(ulong baseAddress, uint irq, uint mmioVersion)
    {
        _baseAddress = baseAddress;
        _irq = irq;
        _mmioVersion = mmioVersion;
        X = 0;
        Y = 0;
    }

    /// <summary>
    /// Finds and creates a virtio mouse device.
    /// Note: QEMU enumerates mouse first, keyboard second, so we find the 1st device.
    /// </summary>
    public static VirtioMouse? FindAndCreate()
    {
        Serial.Write("[VirtioMouse] Searching for virtio-input device (mouse)...\n");

        if (!VirtioMMIO.FindDevice(VirtioMMIO.VIRTIO_DEV_INPUT, out ulong baseAddr, out uint irq))
        {
            Serial.Write("[VirtioMouse] No virtio-input device found\n");
            return null;
        }

        // Read MMIO version (1 = legacy, 2 = modern)
        uint version = VirtioMMIO.Read32(baseAddr, VirtioMMIO.REG_VERSION);

        Serial.Write("[VirtioMouse] Found virtio-input at 0x");
        Serial.WriteHex(baseAddr);
        Serial.Write(" IRQ=");
        Serial.WriteNumber(irq);
        Serial.Write(" MMIO version=");
        Serial.WriteNumber(version);
        Serial.Write("\n");

        var mouse = new VirtioMouse(baseAddr, irq, version);
        Instance = mouse;
        return mouse;
    }

    /// <summary>
    /// Initializes the virtio mouse device.
    /// </summary>
    public override void Initialize()
    {
        Serial.Write("[VirtioMouse] Initializing (MMIO version ");
        Serial.WriteNumber(_mmioVersion);
        Serial.Write(")...\n");

        // Reset device
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, 0);

        // For legacy MMIO (version 1), set guest page size
        if (_mmioVersion == 1)
        {
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_GUEST_PAGE_SIZE, 4096);
        }

        // Set ACKNOWLEDGE status bit
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_ACKNOWLEDGE);

        // Set DRIVER status bit
        uint status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_DRIVER);

        // Read and negotiate features
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DEVICE_FEATURES_SEL, 0);
        uint features = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_DEVICE_FEATURES);
        Serial.Write("[VirtioMouse] Device features: 0x");
        Serial.WriteHex(features);
        Serial.Write("\n");

        // Accept no features
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES_SEL, 0);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES, 0);

        // For modern MMIO, set FEATURES_OK
        if (_mmioVersion >= 2)
        {
            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_FEATURES_OK);

            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            if ((status & VirtioMMIO.STATUS_FEATURES_OK) == 0)
            {
                Serial.Write("[VirtioMouse] ERROR: Device did not accept features\n");
                VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
                return;
            }
        }

        // Set up event queue
        if (!SetupQueue(EVENTQ))
        {
            Serial.Write("[VirtioMouse] ERROR: Failed to setup event queue\n");
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
            return;
        }

        // Allocate event buffers and add to queue
        _eventBuffers = (VirtioInputEvent*)MemoryOp.Alloc((uint)(NUM_EVENT_BUFFERS * sizeof(VirtioInputEvent)));
        for (int i = 0; i < NUM_EVENT_BUFFERS; i++)
        {
            AddEventBuffer(i);
        }

        // Notify device
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, EVENTQ);

        // Set DRIVER_OK
        status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_DRIVER_OK);

        _initialized = true;
        Serial.Write("[VirtioMouse] Initialization complete\n");
    }

    private bool SetupQueue(int queueIndex)
    {
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_SEL, (uint)queueIndex);

        if (_mmioVersion == 1)
        {
            uint pfn = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN);
            if (pfn != 0)
            {
                Serial.Write("[VirtioMouse] Queue already in use\n");
                return false;
            }
        }
        else
        {
            uint ready = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_READY);
            if (ready != 0)
            {
                Serial.Write("[VirtioMouse] Queue already in use\n");
                return false;
            }
        }

        uint maxSize = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM_MAX);
        if (maxSize == 0)
        {
            Serial.Write("[VirtioMouse] Queue not available\n");
            return false;
        }

        uint queueSize = maxSize < QUEUE_SIZE ? maxSize : QUEUE_SIZE;
        _eventQueue = new Virtqueue(queueSize);

        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM, queueSize);

        if (_mmioVersion == 1)
        {
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_ALIGN, 4096);
            ulong baseAddr = _eventQueue.QueueBaseAddr;
            uint pfn = (uint)(baseAddr / 4096);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN, pfn);
        }
        else
        {
            ulong descAddr = _eventQueue.DescriptorTableAddr;
            ulong availAddr = _eventQueue.AvailableRingAddr;
            ulong usedAddr = _eventQueue.UsedRingAddr;

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_LOW, (uint)descAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_HIGH, (uint)(descAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_LOW, (uint)availAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_HIGH, (uint)(availAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_LOW, (uint)usedAddr);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_HIGH, (uint)(usedAddr >> 32));

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
    /// Registers the IRQ handler for mouse interrupts.
    /// </summary>
    public void RegisterIRQHandler()
    {
        Serial.Write("[VirtioMouse] Registering IRQ handler for INTID ");
        Serial.WriteNumber(_irq);
        Serial.Write("\n");

        GIC.ConfigureInterrupt(_irq, true);
        GIC.SetPriority(_irq, 0x80);
        GIC.EnableInterrupt(_irq);
        InterruptManager.SetHandler((byte)_irq, HandleIRQ);

        Serial.Write("[VirtioMouse] IRQ handler registered\n");
    }

    private static void HandleIRQ(ref IRQContext ctx)
    {
        if (Instance == null || !Instance._initialized)
            return;

        // Acknowledge interrupt
        uint intStatus = VirtioMMIO.Read32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);
        VirtioMMIO.Write32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_ACK, intStatus);

        //Serial.Write("[VirtioMouse] IRQ! status=0x");
        //Serial.WriteHex(intStatus);
        //Serial.Write("\n");

        Instance.ProcessEvents();
    }

    private void ProcessEvents()
    {
        if (_eventQueue == null)
            return;

        while (_eventQueue.HasUsedBuffers())
        {
            if (!_eventQueue.GetUsedBuffer(out uint id, out uint len))
                break;

            VirtioInputEvent* evt = &_eventBuffers[id];

            if (evt->Type == EV_REL)
            {
                if (evt->Code == REL_X)
                {
                    _tempDeltaX = (int)evt->Value;
                    _hasEvents = true;
                }
                else if (evt->Code == REL_Y)
                {
                    _tempDeltaY = (int)evt->Value;
                    _hasEvents = true;
                }
            }
            else if (evt->Type == EV_KEY)
            {
                bool pressed = evt->Value != 0;
                if (evt->Code == BTN_LEFT)
                {
                    _tempLeftButton = pressed;
                    _hasEvents = true;
                }
                else if (evt->Code == BTN_RIGHT)
                {
                    _tempRightButton = pressed;
                    _hasEvents = true;
                }
                else if (evt->Code == BTN_MIDDLE)
                {
                    _tempMiddleButton = pressed;
                    _hasEvents = true;
                }
            }
            else if (evt->Type == EV_SYN && _hasEvents)
            {
                // Sync event - dispatch accumulated changes
                X += _tempDeltaX;
                Y += _tempDeltaY;
                LeftButton = _tempLeftButton;
                RightButton = _tempRightButton;
                MiddleButton = _tempMiddleButton;

                OnMouseEvent?.Invoke(_tempDeltaX, _tempDeltaY, _tempLeftButton, _tempRightButton, _tempMiddleButton);

                _tempDeltaX = 0;
                _tempDeltaY = 0;
                _hasEvents = false;
            }

            // Re-add buffer
            _eventQueue.FreeDescriptor((int)id);
            AddEventBuffer((int)id);

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, EVENTQ);
        }
    }

    public override void Poll()
    {
        if (!_initialized || _eventQueue == null)
            return;

        uint intStat = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);

        if (intStat != 0 || _eventQueue.HasUsedBuffers())
        {
            if (intStat != 0)
            {
                VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_INTERRUPT_ACK, intStat);
            }

            ProcessEvents();
        }
    }

    public override void Enable()
    {
        if (!_irqRegistered && _initialized)
        {
            RegisterIRQHandler();
            _irqRegistered = true;
        }
    }

    public override void Disable()
    {
        // Not implemented
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VirtioInputEvent
    {
        public ushort Type;
        public ushort Code;
        public uint Value;
    }
}
