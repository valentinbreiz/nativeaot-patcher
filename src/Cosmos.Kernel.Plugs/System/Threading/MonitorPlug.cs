using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(Monitor))]
public static class MonitorPlug
{
    [PlugMember]
    public static void Enter(object obj)
    {
        MonitorImpl.Enter(obj);
    }

    [PlugMember]
    public static void Enter(object obj, ref bool lockTaken)
    {
        if (lockTaken)
        {
            throw new ArgumentException("lockTaken must be false", nameof(lockTaken));
        }

        MonitorImpl.Enter(obj);
        lockTaken = true;
    }

    [PlugMember]
    public static void Exit(object obj)
    {
        MonitorImpl.Exit(obj);
    }

    [PlugMember]
    public static bool TryEnter(object obj)
    {
        return MonitorImpl.TryEnter(obj);
    }

    [PlugMember]
    public static void TryEnter(object obj, ref bool lockTaken)
    {
        if (lockTaken)
        {
            throw new ArgumentException("lockTaken must be false", nameof(lockTaken));
        }

        lockTaken = MonitorImpl.TryEnter(obj);
    }

    [PlugMember]
    public static bool TryEnter(object obj, int millisecondsTimeout)
    {
        return MonitorImpl.TryEnter(obj);
    }

    [PlugMember]
    public static void TryEnter(object obj, int millisecondsTimeout, ref bool lockTaken)
    {
        if (lockTaken)
        {
            throw new ArgumentException("lockTaken must be false", nameof(lockTaken));
        }

        lockTaken = MonitorImpl.TryEnter(obj);
    }

    [PlugMember]
    public static bool IsEntered(object obj)
    {
        return MonitorImpl.IsEntered(obj);
    }

    [PlugMember]
    public static bool Wait(object obj, int millisecondsTimeout)
    {
        // TODO: Implement condition variable support
        return false;
    }

    [PlugMember]
    public static void Pulse(object obj)
    {
        // TODO: Implement condition variable support
    }

    [PlugMember]
    public static void PulseAll(object obj)
    {
        // TODO: Implement condition variable support
    }

    [PlugMember("get_LockContentionCount")]
    public static long get_LockContentionCount()
    {
        return 0;
    }
}
