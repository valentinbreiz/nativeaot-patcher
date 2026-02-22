// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Input;

namespace Cosmos.Kernel.HAL.X64.Devices.Input;

/// <summary>
/// PS/2 Mouse driver.
/// </summary>
public class PS2Mouse : MouseDevice
{
    private enum Command : byte
    {
        SetDefaults = 0xF6,
        EnableDataReporting = 0xF4,
        DisableDataReporting = 0xF5,
        SetSampleRate = 0xF3,
        GetDeviceID = 0xF2,
        Reset = 0xFF
    }

    // Static reference to the first mouse instance (for IRQ handler)
    private static PS2Mouse? _instance;

    // Flag to prevent multiple IRQ registrations
    private static bool _irqRegistered;

    // Mouse packet buffer (3 bytes for standard PS/2 mouse, 4 bytes for scroll wheel)
    private static byte[] _packet = new byte[4];
    private static int _packetIndex = 0;
    private static bool _hasScrollWheel = false;

    /// <summary>
    /// Registers IRQ handler for mouse interrupts.
    /// Called explicitly from Kernel.Initialize to ensure proper initialization order.
    /// </summary>
    public static void RegisterIRQHandler()
    {
        if (PS2Controller.Instance == null)
        {
            Serial.WriteString("[PS2Mouse] WARNING: PS2Controller instance is null!\n");
            return;
        }

        // Drain any pending data from buffer
        DrainBuffer();

        // Register the IRQ handler and route through APIC (start masked)
        InterruptManager.SetIrqHandler(12, StaticHandleIRQ, startMasked: true);

        // Enable mouse data reporting
        PS2Controller.Instance.PrepareSecondPortWrite();
        PS2Controller.Instance.WaitToWrite();
        Native.IO.Write8(PS2Ports.Data, (byte)Command.EnableDataReporting);
        PS2Controller.Instance.WaitForAck();

        // Drain any remaining data after ACK
        while ((Native.IO.Read8(PS2Ports.Status) & 0x01) != 0)
        {
            Native.IO.Read8(PS2Ports.Data);
        }

        // Enable PS/2 controller IRQ generation for port 2
        PS2Controller.Instance.EnablePortInterrupt(2);

        // Unmask IRQ12 at I/O APIC - ready for mouse events
        PS2Controller.Instance.UnmaskPortInterrupt(2);

        Serial.WriteString("[PS2Mouse] Mouse ready for input\n");
    }

    /// <summary>
    /// The PS/2 port this mouse is connected to (should be 2).
    /// </summary>
    public byte PS2Port { get; }

    private readonly PS2Controller _controller;

    /// <summary>
    /// Creates a new PS/2 mouse instance.
    /// </summary>
    /// <param name="port">The PS/2 port (should be 2 for mouse).</param>
    /// <param name="controller">The PS/2 controller managing this mouse.</param>
    internal PS2Mouse(byte port, PS2Controller controller)
    {
        PS2Port = port;
        _controller = controller;
        X = 0;
        Y = 0;
    }

    /// <summary>
    /// Initializes the mouse.
    /// </summary>
    public override void Initialize()
    {
        Serial.WriteString("[PS2Mouse] Initializing on port ");
        Serial.WriteNumber(PS2Port);
        Serial.WriteString("...\n");

        // Reset mouse
        SendCommand(Command.Reset);
        _controller.WaitForDeviceReset();

        // Set defaults
        SendCommand(Command.SetDefaults);

        // Try to enable scroll wheel (IntelliMouse protocol)
        _hasScrollWheel = TryEnableScrollWheel();

        if (_hasScrollWheel)
        {
            Serial.WriteString("[PS2Mouse] Scroll wheel enabled\n");
        }

        Serial.WriteString("[PS2Mouse] Initialized (data reporting will be enabled later)\n");

        // Store first mouse instance for IRQ handler
        if (_instance == null)
        {
            _instance = this;
        }
    }

    /// <summary>
    /// Tries to enable scroll wheel (IntelliMouse extension).
    /// </summary>
    private bool TryEnableScrollWheel()
    {
        // Magic knock sequence to enable scroll wheel: set sample rate to 200, 100, 80
        SendCommand(Command.SetSampleRate, 200);
        SendCommand(Command.SetSampleRate, 100);
        SendCommand(Command.SetSampleRate, 80);

        // Get device ID - should be 0x03 if scroll wheel is supported
        SendCommand(Command.GetDeviceID);
        byte deviceId = _controller.ReadByteAfterAck();

        return deviceId == 0x03;
    }

    /// <summary>
    /// Static IRQ handler to avoid delegate allocation.
    /// </summary>
    private static void StaticHandleIRQ(ref IRQContext context)
    {
        // Read byte from port 0x60
        byte data = Native.IO.Read8(0x60);

        // Add to packet buffer
        _packet[_packetIndex] = data;
        _packetIndex++;

        // Check if we have a complete packet
        int packetSize = _hasScrollWheel ? 4 : 3;
        if (_packetIndex >= packetSize)
        {
            _packetIndex = 0;

            // Validate packet (bit 3 of first byte should always be 1)
            if ((_packet[0] & 0x08) != 0x08)
            {
                // Invalid packet, skip it
                return;
            }

            // Parse packet
            bool leftButton = (_packet[0] & 0x01) != 0;
            bool rightButton = (_packet[0] & 0x02) != 0;
            bool middleButton = (_packet[0] & 0x04) != 0;

            // X/Y movement (9-bit signed values)
            int deltaX = _packet[1];
            int deltaY = _packet[2];

            // Sign-extend X and Y if negative
            if ((_packet[0] & 0x10) != 0)
            {
                deltaX |= unchecked((int)0xFFFFFF00);
            }
            if ((_packet[0] & 0x20) != 0)
            {
                deltaY |= unchecked((int)0xFFFFFF00);
            }

            // Y is inverted on PS/2 mice
            deltaY = -deltaY;

            // Update instance state
            if (_instance != null)
            {
                _instance.X += deltaX;
                _instance.Y += deltaY;
                _instance.LeftButton = leftButton;
                _instance.RightButton = rightButton;
                _instance.MiddleButton = middleButton;

                // Invoke callback
                _instance.OnMouseEvent?.Invoke(deltaX, deltaY, leftButton, rightButton, middleButton);
            }
        }

        // EOI is sent by InterruptManager.Dispatch after this handler returns
    }

    /// <summary>
    /// Drains any pending data from the mouse buffer.
    /// </summary>
    public static void DrainBuffer()
    {
        int count = 0;
        while ((Native.IO.Read8(PS2Ports.Status) & 0x01) != 0 && count < 100)
        {
            Native.IO.Read8(PS2Ports.Data);
            count++;
        }
    }

    /// <summary>
    /// Check if mouse data is available.
    /// </summary>
    public override bool DataAvailable => (Native.IO.Read8(PS2Ports.Status) & 0x20) != 0;

    /// <summary>
    /// Enable mouse and register IRQ handler if not already done.
    /// </summary>
    public override void Enable()
    {
        // Register IRQ handler on first Enable() call (after callback is set)
        if (!_irqRegistered)
        {
            RegisterIRQHandler();
            _irqRegistered = true;
            return;
        }

        SendCommand(Command.EnableDataReporting);
    }

    /// <summary>
    /// Disable mouse data reporting.
    /// </summary>
    public override void Disable()
    {
        SendCommand(Command.DisableDataReporting);
    }

    private void SendCommand(Command command, byte? data = null)
    {
        if (PS2Port == 2)
        {
            _controller.PrepareSecondPortWrite();
        }

        _controller.WaitToWrite();
        Native.IO.Write8(PS2Ports.Data, (byte)command);

        _controller.WaitForAck();

        if (data.HasValue)
        {
            if (PS2Port == 2)
            {
                _controller.PrepareSecondPortWrite();
            }

            _controller.WaitToWrite();
            Native.IO.Write8(PS2Ports.Data, data.Value);

            _controller.WaitForAck();
        }
    }
}
