// This code is licensed under MIT license (see LICENSE for details)

using System.Collections;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory;

public unsafe struct FrozenArray<T> : IDisposable where T : unmanaged
{
    public readonly T[] Array;     // The managed array
    public readonly T* Pointer;    // Stable pointer to first element

    private GCHandle _handle;
    private bool _disposed;

    public int Length => Array.Length;
    public int Count => Array.Length;

    public FrozenArray(T[] array)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        Array = array;

        // Pin the array in memory (GC will never move it)
        _handle = GCHandle.Alloc(Array, GCHandleType.Pinned);

        // Get a pointer to the first element
        Pointer = (T*)_handle.AddrOfPinnedObject();
    }

    public readonly ref T this[int index]
    {
        get => ref Array[index];
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _handle.Free();
            _disposed = true;
        }
    }

}
