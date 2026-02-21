// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Network;

/// <summary>
/// VirtIO Network Device Driver for ARM64 using MMIO transport.
/// Works with QEMU virt machine virtio-net-device.
/// </summary>
public unsafe class VirtioNet : INetworkDevice
{
    // --- Constants ---

    // Virtio-net feature bits
    private const uint VIRTIO_NET_F_CSUM = 1 << 0;
    private const uint VIRTIO_NET_F_MAC = 1 << 5;
    private const uint VIRTIO_NET_F_STATUS = 1 << 16;
    private const uint VIRTIO_NET_S_LINK_UP = 1;

    // Queue indices
    private const int RX_QUEUE = 0;
    private const int TX_QUEUE = 1;

    private const int VIRTIO_NET_HDR_SIZE = 10;
    private const uint QUEUE_SIZE = 128;
    private const int RX_BUFFER_SIZE = 2048;

    // --- Private fields ---

    private readonly ulong _baseAddress;
    private readonly uint _irq;
    private readonly uint _mmioVersion;
    private MACAddress _macAddress;
    private bool _networkInitialized;
    private bool _linkUp;
    private bool _enabled;

    private Virtqueue? _rxQueue;
    private Virtqueue? _txQueue;

    private byte** _rxBuffers;
    private byte** _txBuffers;

    // --- Properties ---

    public static VirtioNet? Instance { get; private set; }
    public PacketReceivedHandler? OnPacketReceived { get; set; }
    string INetworkDevice.Name => "VirtioNet";
    public MACAddress MacAddress => _macAddress;
    public bool LinkUp => _linkUp;
    public bool Ready => _networkInitialized;

    // --- Constructor ---

    private VirtioNet(ulong baseAddress, uint irq, uint mmioVersion)
    {
        _baseAddress = baseAddress;
        _irq = irq;
        _mmioVersion = mmioVersion;
        _macAddress = MACAddress.None;
        _networkInitialized = false;
        _linkUp = false;
        _enabled = false;
        Instance = this;
    }

    // --- Public methods ---

    public static VirtioNet? FindAndCreate()
    {
        Serial.Write("[VirtioNet] Searching for virtio-net device...\n");

        if (!VirtioMMIO.FindDevice(VirtioMMIO.VIRTIO_DEV_NET, out ulong baseAddr, out uint irq))
        {
            Serial.Write("[VirtioNet] No virtio-net device found\n");
            return null;
        }

        uint version = VirtioMMIO.Read32(baseAddr, VirtioMMIO.REG_VERSION);

        Serial.Write("[VirtioNet] Found virtio-net at 0x");
        Serial.WriteHex(baseAddr);
        Serial.Write(" IRQ=");
        Serial.WriteNumber(irq);
        Serial.Write(" MMIO version=");
        Serial.WriteNumber(version);
        Serial.Write("\n");

        return new VirtioNet(baseAddr, irq, version);
    }

    /// <summary>
    /// INetworkDevice.Initialize implementation.
    /// </summary>
    void INetworkDevice.Initialize() => InitializeNetwork();


    public void InitializeNetwork()
    {
        if (_networkInitialized) return;

        Serial.Write("[VirtioNet] Initializing (MMIO version ");
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
        uint deviceFeatures = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_DEVICE_FEATURES);

        Serial.Write("[VirtioNet] Device features: 0x");
        Serial.WriteHex(deviceFeatures);
        Serial.Write("\n");

        // Accept MAC and STATUS features
        uint guestFeatures = 0;
        if ((deviceFeatures & VIRTIO_NET_F_MAC) != 0)
        {
            guestFeatures |= VIRTIO_NET_F_MAC;
            Serial.Write("[VirtioNet] Accepting VIRTIO_NET_F_MAC\n");
        }
        if ((deviceFeatures & VIRTIO_NET_F_STATUS) != 0)
        {
            guestFeatures |= VIRTIO_NET_F_STATUS;
            Serial.Write("[VirtioNet] Accepting VIRTIO_NET_F_STATUS\n");
        }

        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES_SEL, 0);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_DRIVER_FEATURES, guestFeatures);

        // For modern MMIO (version 2), need to set FEATURES_OK
        if (_mmioVersion >= 2)
        {
            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_FEATURES_OK);

            status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
            if ((status & VirtioMMIO.STATUS_FEATURES_OK) == 0)
            {
                Serial.Write("[VirtioNet] ERROR: Device did not accept features\n");
                VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
                return;
            }
        }

        // Setup RX and TX queues
        if (!SetupQueue(RX_QUEUE, out _rxQueue) || !SetupQueue(TX_QUEUE, out _txQueue))
        {
            Serial.Write("[VirtioNet] ERROR: Failed to setup queues\n");
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, VirtioMMIO.STATUS_FAILED);
            return;
        }

        InitializeRxBuffers();
        InitializeTxBuffers();

        // Read MAC address from device config space
        if ((guestFeatures & VIRTIO_NET_F_MAC) != 0)
        {
            byte[] mac = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                mac[i] = VirtioMMIO.Read8(_baseAddress, VirtioMMIO.REG_CONFIG + (uint)i);
            }
            _macAddress = new MACAddress(mac);
            Serial.Write("[VirtioNet] MAC address: ");
            Serial.WriteString(_macAddress.ToString());
            Serial.Write("\n");
        }

        // Register IRQ handler
        RegisterIRQHandler();

        // Set DRIVER_OK to complete initialization
        status = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_STATUS);
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_STATUS, status | VirtioMMIO.STATUS_DRIVER_OK);

        // Check link status
        if ((guestFeatures & VIRTIO_NET_F_STATUS) != 0)
        {
            ushort linkStatus = VirtioMMIO.Read16(_baseAddress, VirtioMMIO.REG_CONFIG + 6);
            _linkUp = (linkStatus & VIRTIO_NET_S_LINK_UP) != 0;
            Serial.Write("[VirtioNet] Link status: ");
            Serial.WriteString(_linkUp ? "UP" : "DOWN");
            Serial.Write("\n");
        }
        else
        {
            _linkUp = true;
        }

        _networkInitialized = true;
        _enabled = true;
        Serial.Write("[VirtioNet] Initialization complete\n");
    }

    public void RegisterIRQHandler()
    {
        Serial.Write("[VirtioNet] Registering IRQ handler for INTID ");
        Serial.WriteNumber(_irq);
        Serial.Write("\n");

        // Configure interrupt as edge-triggered (VirtIO MMIO uses edge)
        GIC.ConfigureInterrupt(_irq, true);

        // Set priority and enable
        GIC.SetPriority(_irq, 0x80);
        GIC.EnableInterrupt(_irq);

        // Register handler
        InterruptManager.SetHandler((byte)_irq, HandleIRQ);

        Serial.Write("[VirtioNet] IRQ handler registered\n");
    }

    public bool Send(byte[] data, int length)
    {
        if (!_networkInitialized || !_enabled || _txQueue == null || _txBuffers == null || data == null)
            return false;

        ReclaimTx();

        int descIdx = _txQueue.AllocDescriptor();
        if (descIdx < 0)
        {
            Serial.Write("[VirtioNet] No TX descriptors available\n");
            return false;
        }

        if (length > RX_BUFFER_SIZE - VIRTIO_NET_HDR_SIZE)
            length = RX_BUFFER_SIZE - VIRTIO_NET_HDR_SIZE;

        byte* buf = _txBuffers[descIdx];

        // Clear virtio-net header
        for (int i = 0; i < VIRTIO_NET_HDR_SIZE; i++)
            buf[i] = 0;

        // Copy packet data
        for (int i = 0; i < length; i++)
            buf[VIRTIO_NET_HDR_SIZE + i] = data[i];

        _txQueue.SetupDescriptor(descIdx, (ulong)buf, (uint)(VIRTIO_NET_HDR_SIZE + length), 0, 0);
        _txQueue.AddAvailable((ushort)descIdx);

        // Notify device
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, TX_QUEUE);

        return true;
    }

    public void Enable() => _enabled = true;
    public void Disable() => _enabled = false;

    // --- Private methods ---

    private bool SetupQueue(int queueIndex, out Virtqueue? queue)
    {
        queue = null;

        Serial.Write("[VirtioNet] Setting up queue ");
        Serial.WriteNumber((uint)queueIndex);
        Serial.Write("...\n");

        // Select queue
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_SEL, (uint)queueIndex);

        // Check if queue is available
        if (_mmioVersion == 1)
        {
            uint pfn = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN);
            if (pfn != 0)
            {
                Serial.Write("[VirtioNet] Queue already in use\n");
                return false;
            }
        }
        else
        {
            uint ready = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_READY);
            if (ready != 0)
            {
                Serial.Write("[VirtioNet] Queue already in use\n");
                return false;
            }
        }

        // Get max queue size
        uint maxSize = VirtioMMIO.Read32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM_MAX);
        if (maxSize == 0)
        {
            Serial.Write("[VirtioNet] Queue not available\n");
            return false;
        }

        uint queueSize = maxSize < QUEUE_SIZE ? maxSize : QUEUE_SIZE;
        Serial.Write("[VirtioNet] Queue size: ");
        Serial.WriteNumber(queueSize);
        Serial.Write("\n");

        // Create virtqueue
        queue = new Virtqueue(queueSize);

        // Set queue size
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NUM, queueSize);

        if (_mmioVersion == 1)
        {
            // Legacy: set queue alignment and PFN
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_ALIGN, 4096);

            ulong baseAddr = queue.QueueBaseAddr;
            uint pfn = (uint)(baseAddr / 4096);

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_PFN, pfn);
        }
        else
        {
            // Modern: set queue addresses
            ulong descAddr = queue.DescriptorTableAddr;
            ulong availAddr = queue.AvailableRingAddr;
            ulong usedAddr = queue.UsedRingAddr;

            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_LOW, (uint)(descAddr & 0xFFFFFFFF));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DESC_HIGH, (uint)(descAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_LOW, (uint)(availAddr & 0xFFFFFFFF));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DRIVER_HIGH, (uint)(availAddr >> 32));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_LOW, (uint)(usedAddr & 0xFFFFFFFF));
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_DEVICE_HIGH, (uint)(usedAddr >> 32));

            // Mark queue as ready
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_READY, 1);
        }

        return true;
    }

    private void InitializeRxBuffers()
    {
        if (_rxQueue == null) return;

        Serial.Write("[VirtioNet] Initializing RX buffers...\n");

        _rxBuffers = (byte**)MemoryOp.Alloc((uint)(_rxQueue.QueueSize * sizeof(byte*)));
        for (int i = 0; i < _rxQueue.QueueSize; i++)
        {
            _rxBuffers[i] = (byte*)MemoryOp.Alloc(RX_BUFFER_SIZE);
            int descIdx = _rxQueue.AllocDescriptor();
            if (descIdx < 0) break;

            _rxQueue.SetupDescriptor(descIdx, (ulong)_rxBuffers[i], RX_BUFFER_SIZE,
                Virtqueue.VRING_DESC_F_WRITE, 0);
            _rxQueue.AddAvailable((ushort)descIdx);
        }

        // Notify device that RX buffers are available
        VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, RX_QUEUE);

        Serial.Write("[VirtioNet] RX buffers initialized\n");
    }

    private void InitializeTxBuffers()
    {
        if (_txQueue == null) return;

        Serial.Write("[VirtioNet] Initializing TX buffers...\n");

        _txBuffers = (byte**)MemoryOp.Alloc((uint)(_txQueue.QueueSize * sizeof(byte*)));
        for (int i = 0; i < _txQueue.QueueSize; i++)
        {
            _txBuffers[i] = (byte*)MemoryOp.Alloc(RX_BUFFER_SIZE);
        }

        Serial.Write("[VirtioNet] TX buffers initialized\n");
    }

    private static void HandleIRQ(ref IRQContext context)
    {
        if (Instance == null || !Instance._networkInitialized)
            return;

        // Read and acknowledge interrupt
        uint intStatus = VirtioMMIO.Read32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_STATUS);
        VirtioMMIO.Write32(Instance._baseAddress, VirtioMMIO.REG_INTERRUPT_ACK, intStatus);

        Serial.Write("[VirtioNet] IRQ! status=0x");
        Serial.WriteHex(intStatus);
        Serial.Write("\n");

        // Process used buffers
        if ((intStatus & 1) != 0)  // Used buffer notification
        {
            Instance.ProcessRx();
            Instance.ReclaimTx();
        }
    }

    private void ProcessRx()
    {
        if (_rxQueue == null || _rxBuffers == null) return;

        Serial.Write("[VirtioNet] Processing RX...\n");

        bool received = false;
        while (_rxQueue.GetUsedBuffer(out uint id, out uint len))
        {
            Serial.Write("[VirtioNet] RX buffer id=");
            Serial.WriteNumber(id);
            Serial.Write(" len=");
            Serial.WriteNumber(len);
            Serial.Write("\n");

            if (id < _rxQueue.QueueSize && len > VIRTIO_NET_HDR_SIZE)
            {
                uint dataLen = len - VIRTIO_NET_HDR_SIZE;
                byte[] data = new byte[dataLen];
                byte* src = _rxBuffers[id] + VIRTIO_NET_HDR_SIZE;
                for (int i = 0; i < dataLen; i++)
                    data[i] = src[i];

                OnPacketReceived?.Invoke(data, (int)dataLen);
                received = true;
            }

            // Return buffer to available ring
            if (id < _rxQueue.QueueSize)
            {
                _rxQueue.SetupDescriptor((int)id, (ulong)_rxBuffers[id], RX_BUFFER_SIZE,
                    Virtqueue.VRING_DESC_F_WRITE, 0);
                _rxQueue.AddAvailable((ushort)id);
            }
        }

        if (received)
        {
            // Notify device that new RX buffers are available
            VirtioMMIO.Write32(_baseAddress, VirtioMMIO.REG_QUEUE_NOTIFY, RX_QUEUE);
        }
    }

    private void ReclaimTx()
    {
        if (_txQueue == null) return;

        while (_txQueue.GetUsedBuffer(out uint id, out uint len))
        {
            if (id < _txQueue.QueueSize)
            {
                _txQueue.FreeDescriptor((int)id);
            }
        }
    }
}
