// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.ARM64.Devices.Input;

/// <summary>
/// Virtio-input protocol constants (virtio spec 5.8) and the Linux evdev event codes
/// (linux/input-event-codes.h) that virtio-input passes through verbatim.
/// </summary>
internal static class VirtioInput
{
    // Virtio-input virtqueue indexes (virtio spec 5.8.2)

    /// <summary>Virtqueue 0: eventq, the device sends input events to the driver.</summary>
    internal const int EVENTQ = 0;

    /// <summary>Virtqueue 1: statusq, the driver sends status changes (e.g. keyboard LEDs) to the device; not yet implemented.</summary>
    internal const int STATUSQ = 1;

    // Linux evdev event types

    /// <summary>Synchronization event: marks the end of a batch of input events.</summary>
    internal const ushort EV_SYN = 0x00;

    /// <summary>Key or button state change event.</summary>
    internal const ushort EV_KEY = 0x01;

    /// <summary>Relative axis movement event (mouse motion, wheel).</summary>
    internal const ushort EV_REL = 0x02;

    /// <summary>Absolute axis position event (touchscreen, tablet).</summary>
    internal const ushort EV_ABS = 0x03;
}
