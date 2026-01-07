// This code is licensed under MIT license (see LICENSE for details)
// Ported from Cosmos.HAL2/PIT.cs

using System;
using System.Collections.Generic;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Timer;

namespace Cosmos.Kernel.HAL.X64.Devices.Timer;

/// <summary>
/// Handles the Programmable Interval Timer (PIT).
/// </summary>
public class PIT : TimerDevice
{
    /// <summary>
    /// Represents a virtual timer that can be handled using the
    /// Programmable Interrupt Timer (PIT).
    /// </summary>
    public class PITTimer : IDisposable
    {
        internal ulong NSRemaining;
        internal int ID = -1;

        /// <summary>
        /// The delay between each timer cycle.
        /// </summary>
        public ulong NanosecondsTimeout;

        /// <summary>
        /// Whether this timer will fire once, or will fire indefinetly until unregistered.
        /// </summary>
        public bool Recurring;

        /// <summary>
        /// The ID of the timer.
        /// </summary>
        public int TimerID => ID;

        /// <summary>
        /// The method to invoke for each cycle of the timer.
        /// </summary>
        public OnTrigger HandleTrigger;

        /// <summary>
        /// Represents the trigger handler for a <see cref="PITTimer"/>.
        /// </summary>
        /// <param name="irqContext">The state of the CPU when the PIT interrupt has occured.</param>
        public delegate void OnTrigger(ref IRQContext irqContext);

        /// <summary>
        /// Initializes a new <see cref="PITTimer"/>, with the specified
        /// callback method and properties.
        /// </summary>
        /// <param name="callback">The method to invoke for each timer cycle.</param>
        /// <param name="nanosecondsTimeout">The delay between timer cycles.</param>
        /// <param name="recurring">Whether this timer will fire once, or will fire indefinetly until unregistered.</param>
        public PITTimer(OnTrigger callback, ulong nanosecondsTimeout, bool recurring)
        {
            HandleTrigger = callback;
            NanosecondsTimeout = nanosecondsTimeout;
            NSRemaining = NanosecondsTimeout;
            Recurring = recurring;
        }

        /// <inheritdoc cref="PITTimer(OnTrigger, ulong, bool)"/>
        public PITTimer(Action callback, ulong nanosecondsTimeout, bool recurring)
            : this((ref IRQContext _) => callback(), nanosecondsTimeout, recurring)
        { }

        /// <summary>
        /// Initializes a new recurring <see cref="PITTimer"/>, with the specified
        /// callback method and amount of nanoseconds left until the next timer cycle.
        /// </summary>
        /// <param name="callback">The method to invoke for each timer cycle.</param>
        /// <param name="nanosecondsTimeout">The delay between timer cycles.</param>
        /// <param name="nanosecondsLeft">The amount of time left before the first timer cycle is fired.</param>
        public PITTimer(OnTrigger callback, ulong nanosecondsTimeout, ulong nanosecondsLeft)
        {
            HandleTrigger = callback;
            NanosecondsTimeout = nanosecondsTimeout;
            NSRemaining = nanosecondsLeft;
            Recurring = true;
        }

        /// <inheritdoc cref="PITTimer(OnTrigger, ulong, ulong)"/>
        public PITTimer(Action callback, ulong nanosecondsTimeout, ulong nanosecondsLeft)
            : this((ref IRQContext _) => callback(), nanosecondsTimeout, nanosecondsLeft)
        { }

        ~PITTimer()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (ID != -1)
            {
                Instance?.UnregisterTimer(ID);
            }
        }
    }

    /// <summary>
    /// Singleton instance of the PIT.
    /// </summary>
    public static PIT? Instance { get; private set; }

    public const uint PITFrequency = 1193180;
    public const uint PITDelayNS = 838;

    public bool T0RateGen = false;

    private readonly List<PITTimer> activeHandlers = new();
    private ushort t0Countdown = 65535;
    private ushort t2Countdown = 65535;
    private int timerCounter;

    /// <summary>
    /// Channel 0 data port.
    /// </summary>
    public const ushort Data0 = 0x40;
    /// <summary>
    /// Channel 1 data port.
    /// </summary>
    public const ushort Data1 = 0x41;
    /// <summary>
    /// Channel 2 data port.
    /// </summary>
    public const ushort Data2 = 0x42;
    /// <summary>
    /// Command register port.
    /// </summary>
    public const ushort Command = 0x43;

    public PIT()
    {
    }

    /// <summary>
    /// Initialize the PIT device.
    /// </summary>
    public override void Initialize()
    {
        Serial.Write("[PIT] Initializing...\n");

        Instance = this;
        T0Countdown = 65535;

        Serial.Write("[PIT] Initialized\n");
    }

    /// <summary>
    /// Registers the IRQ handler for the PIT.
    /// Should be called after interrupt system is ready.
    /// </summary>
    public void RegisterIRQHandler()
    {
        Serial.Write("[PIT] Registering IRQ 0 handler...\n");
        InterruptManager.SetIrqHandler(0, HandleIRQ);
        Serial.Write("[PIT] IRQ 0 handler registered\n");
    }

    public ushort T0Countdown
    {
        get => t0Countdown;
        set
        {
            t0Countdown = value;

            Native.IO.Write8(Command, (byte)(T0RateGen ? 0x34 : 0x30));
            Native.IO.Write8(Data0, (byte)(value & 0xFF));
            Native.IO.Write8(Data0, (byte)(value >> 8));
        }
    }

    /// <summary>
    /// Gets the timer frequency in Hz.
    /// </summary>
    public override uint Frequency => PITFrequency / t0Countdown;

    public uint T0Frequency
    {
        get => PITFrequency / t0Countdown;
        set
        {
            if (value < 19 || value > 1193180)
            {
                Serial.Write("[PIT] ERROR: Frequency must be between 19 and 1193180!\n");
                return;
            }

            T0Countdown = (ushort)(PITFrequency / value);
        }
    }

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    /// <param name="frequency">Frequency in Hz.</param>
    public override void SetFrequency(uint frequency)
    {
        T0Frequency = frequency;
    }

    public uint T0DelayNS
    {
        get => PITDelayNS * t0Countdown;
        set
        {
            if (value > 54918330)
            {
                Serial.Write("[PIT] ERROR: Delay must be no greater than 54918330!\n");
                return;
            }

            T0Countdown = (ushort)(value / PITDelayNS);
        }
    }

    public ushort T2Countdown
    {
        get => t2Countdown;
        set
        {
            t2Countdown = value;

            Native.IO.Write8(Command, 0xB6);
            Native.IO.Write8(Data2, (byte)(value & 0xFF));
            Native.IO.Write8(Data2, (byte)(value >> 8));
        }
    }

    public uint T2Frequency
    {
        get => PITFrequency / t2Countdown;
        set
        {
            if (value < 19 || value > 1193180)
            {
                Serial.Write("[PIT] ERROR: Frequency must be between 19 and 1193180!\n");
                return;
            }

            T2Countdown = (ushort)(PITFrequency / value);
        }
    }

    public uint T2DelayNS
    {
        get => PITDelayNS * t2Countdown;
        set
        {
            if (value > 54918330)
            {
                Serial.Write("[PIT] ERROR: Delay must be no greater than 54918330!\n");
                return;
            }

            T2Countdown = (ushort)(value / PITDelayNS);
        }
    }

    /// <summary>
    /// Halts the CPU for the specified amount of milliseconds.
    /// </summary>
    /// <param name="timeoutMs">The amount of milliseconds to halt the CPU for.</param>
    public override void Wait(uint timeoutMs)
    {
        // Register timer and wait for THIS specific timer to complete
        int timerId = RegisterTimer(new PITTimer((ref IRQContext _) => { }, timeoutMs * 1000000UL, false));

        // Wait until our timer is removed from the list (fires and gets cleaned up)
        while (IsTimerActive(timerId))
        {
            PlatformHAL.CpuOps?.Halt();
        }
    }

    /// <summary>
    /// Halts the CPU for the specified amount of nanoseconds.
    /// </summary>
    /// <param name="timeoutNs">The amount of nanoseconds to halt the CPU for.</param>
    public void WaitNS(ulong timeoutNs)
    {
        int timerId = RegisterTimer(new PITTimer((ref IRQContext _) => { }, timeoutNs, false));

        while (IsTimerActive(timerId))
        {
            PlatformHAL.CpuOps?.Halt();
        }
    }

    /// <summary>
    /// Check if a timer with the given ID is still active.
    /// </summary>
    private bool IsTimerActive(int timerId)
    {
        for (int i = 0; i < activeHandlers.Count; i++)
        {
            if (activeHandlers[i].ID == timerId)
                return true;
        }
        return false;
    }

    private static unsafe void HandleIRQ(ref IRQContext aContext)
    {
        if (Instance == null)
            return;

        ulong T0Delay = Instance.T0DelayNS;

        // In one-shot mode, must reload after each interrupt
        if (!Instance.T0RateGen)
            Instance.T0Countdown = Instance.t0Countdown;

        for (int i = Instance.activeHandlers.Count - 1; i >= 0; i--)
        {
            var handler = Instance.activeHandlers[i];

            if (handler.NSRemaining <= T0Delay)
            {
                if (handler.Recurring)
                {
                    handler.NSRemaining = handler.NanosecondsTimeout;
                }
                else
                {
                    handler.ID = -1;
                    Instance.activeHandlers.RemoveAt(i);
                }

                handler.HandleTrigger(ref aContext);
            }
            else
            {
                handler.NSRemaining -= T0Delay;
            }
        }

        // Invoke OnTick handler
        Instance.OnTick?.Invoke();
    }

    /// <summary>
    /// Registers a timer to this <see cref="PIT"/> object.
    /// </summary>
    /// <param name="timer">The target timer.</param>
    /// <returns>The newly assigned ID to the timer.</returns>
    public int RegisterTimer(PITTimer timer)
    {
        if (timer.ID != -1)
        {
            Serial.Write("[PIT] ERROR: Timer has already been registered!\n");
            return -1;
        }

        timer.ID = timerCounter++;
        activeHandlers.Add(timer);
        // Reprogram the PIT to ensure it's running
        T0Countdown = t0Countdown;
        return timer.ID;
    }

    /// <summary>
    /// Unregisters a timer that has been previously registered to this
    /// <see cref="PIT"/> object.
    /// </summary>
    /// <param name="timerId">The ID of the timer to unregister.</param>
    public void UnregisterTimer(int timerId)
    {
        for (int i = 0; i < activeHandlers.Count; i++)
        {
            if (activeHandlers[i].ID == timerId)
            {
                activeHandlers[i].ID = -1;
                activeHandlers.RemoveAt(i);
                return;
            }
        }
    }
}
