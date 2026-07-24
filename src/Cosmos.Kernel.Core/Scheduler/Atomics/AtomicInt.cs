// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Scheduler.Atomics;

public struct AtomicIdULong
{
    private ulong _value;

    public ulong Next()
    {
        return Interlocked.Increment(ref this._value);
    }

}

public struct AtomicIdLong
{
    private long _value;

    public long Next()
    {
        return Interlocked.Increment(ref this._value);
    }

}

public struct AtomicIdUInt
{
    private uint _value;

    public uint Next()
    {
        return Interlocked.Increment(ref this._value);
    }

}

public struct AtomicIdInt
{
    private int _value;

    public int Next()
    {
        return Interlocked.Increment(ref this._value);
    }

}
