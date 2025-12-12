// GC Handle Table Implementation for NativeAOT Kernel
// Provides GCHandle support for managed/unmanaged interop

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GC;

/// <summary>
/// Handle types matching System.Runtime.InteropServices.GCHandleType
/// </summary>
public enum HandleType : byte
{
    /// <summary>Free entry in handle table</summary>
    Free = 0,
    /// <summary>Weak reference - object can be collected</summary>
    Weak = 1,
    /// <summary>Weak reference that tracks resurrection</summary>
    WeakTrackResurrection = 2,
    /// <summary>Strong reference - prevents collection</summary>
    Normal = 3,
    /// <summary>Strong reference + prevents moving (same as Normal for non-moving GC)</summary>
    Pinned = 4,
    /// <summary>Dependent handle - secondary depends on primary</summary>
    Dependent = 5,
    /// <summary>Reference counted handle for COM interop</summary>
    RefCounted = 6
}

/// <summary>
/// Handle entry stored in native memory.
/// Size: 32 bytes aligned for efficient access.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HandleEntry
{
    /// <summary>Object pointer (8 bytes)</summary>
    public void* Target;

    /// <summary>Secondary object for dependent handles (8 bytes)</summary>
    public void* Secondary;

    /// <summary>Handle type (1 byte)</summary>
    public HandleType Type;

    /// <summary>Padding for alignment (1 byte)</summary>
    public byte Padding1;

    /// <summary>Padding for alignment (2 bytes)</summary>
    public short Padding2;

    /// <summary>Next free index in free list, -1 = end (4 bytes)</summary>
    public int NextFreeIndex;

    /// <summary>Padding to align struct to 32 bytes (8 bytes)</summary>
    public long Padding3;
}

/// <summary>
/// Native handle table for GC handle management.
/// Uses PageAllocator for storage to avoid managed heap dependency.
/// Thread-safety prepared but currently single-threaded.
/// </summary>
public static unsafe class HandleTable
{
    private const int InitialCapacity = 256;
    private const int GrowthFactor = 2;
    private static int EntriesPerPage => (int)(PageAllocator.PageSize / (ulong)sizeof(HandleEntry));

    // Native storage - allocated via PageAllocator
    private static HandleEntry* _entries;
    private static int _capacity;
    private static int _freeListHead;  // -1 = no free entries
    private static int _count;
    private static bool _initialized;

    // Future threading support
    // private static SpinLock _lock;

    /// <summary>
    /// Initialize the handle table. Must be called after PageAllocator is initialized.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        Serial.WriteString("[HandleTable] Initializing with capacity: ");
        Serial.WriteNumber(InitialCapacity);
        Serial.WriteString("\n");

        // Calculate pages needed (256 entries * 32 bytes = 8KB = 2 pages)
        int bytesNeeded = InitialCapacity * sizeof(HandleEntry);
        ulong pagesNeeded = ((ulong)bytesNeeded + PageAllocator.PageSize - 1) / PageAllocator.PageSize;

        _entries = (HandleEntry*)PageAllocator.AllocPages(PageType.Unmanaged, pagesNeeded, zero: true);

        if (_entries == null)
        {
            Serial.WriteString("[HandleTable] ERROR: Failed to allocate handle table!\n");
            return;
        }

        _capacity = InitialCapacity;
        _freeListHead = 0;
        _count = 0;

        // Initialize free list - chain all entries together
        for (int i = 0; i < InitialCapacity - 1; i++)
        {
            _entries[i].Type = HandleType.Free;
            _entries[i].Target = null;
            _entries[i].Secondary = null;
            _entries[i].NextFreeIndex = i + 1;
        }

        // Last entry points to -1 (end of list)
        _entries[InitialCapacity - 1].Type = HandleType.Free;
        _entries[InitialCapacity - 1].Target = null;
        _entries[InitialCapacity - 1].Secondary = null;
        _entries[InitialCapacity - 1].NextFreeIndex = -1;

        _initialized = true;

        Serial.WriteString("[HandleTable] Initialized at: 0x");
        Serial.WriteHex((ulong)_entries);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Allocate a handle for an object.
    /// </summary>
    /// <param name="obj">Object to create handle for (can be null for weak handles)</param>
    /// <param name="type">Type of handle to create</param>
    /// <returns>Handle value, or IntPtr.Zero on failure</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr Alloc(object? obj, HandleType type)
    {
        if (!_initialized)
        {
            Serial.WriteString("[HandleTable] ERROR: Not initialized!\n");
            return IntPtr.Zero;
        }

        // Future: _lock.Enter();
        try
        {
            if (_freeListHead == -1)
            {
                // No free entries - grow the table
                if (!Grow())
                {
                    Serial.WriteString("[HandleTable] ERROR: Failed to grow table!\n");
                    return IntPtr.Zero;
                }
            }

            // Get free entry
            int index = _freeListHead;
            ref HandleEntry entry = ref _entries[index];

            // Update free list head
            _freeListHead = entry.NextFreeIndex;

            // Initialize entry
            entry.Type = type;
            entry.Target = obj != null ? Unsafe.AsPointer(ref obj) : null;
            entry.Secondary = null;
            entry.NextFreeIndex = -1;  // Not in free list

            _count++;

            // Encode handle: index shifted to allow for flags in low bits
            // Using index + 1 so that 0 can represent null handle
            return (IntPtr)((index + 1) << 2);
        }
        finally
        {
            // Future: _lock.Exit();
        }
    }

    /// <summary>
    /// Free a previously allocated handle.
    /// </summary>
    /// <param name="handle">Handle to free</param>
    public static void Free(IntPtr handle)
    {
        if (!_initialized || handle == IntPtr.Zero) return;

        // Future: _lock.Enter();
        try
        {
            int index = DecodeHandle(handle);
            if (index < 0 || index >= _capacity) return;

            ref HandleEntry entry = ref _entries[index];

            // Already free?
            if (entry.Type == HandleType.Free) return;

            // Clear and add to free list
            entry.Type = HandleType.Free;
            entry.Target = null;
            entry.Secondary = null;
            entry.NextFreeIndex = _freeListHead;
            _freeListHead = index;

            _count--;
        }
        finally
        {
            // Future: _lock.Exit();
        }
    }

    /// <summary>
    /// Get the object referenced by a handle.
    /// </summary>
    /// <param name="handle">Handle to query</param>
    /// <returns>Object or null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Get(IntPtr handle)
    {
        if (!_initialized || handle == IntPtr.Zero) return null;

        int index = DecodeHandle(handle);
        if (index < 0 || index >= _capacity) return null;

        ref HandleEntry entry = ref _entries[index];
        if (entry.Type == HandleType.Free) return null;

        // For weak handles, would check if object is still alive here
        // For now, just return the target
        if (entry.Target == null) return null;

        return Unsafe.AsRef<object>(entry.Target);
    }

    /// <summary>
    /// Set the object referenced by a handle.
    /// </summary>
    /// <param name="handle">Handle to update</param>
    /// <param name="value">New object value</param>
    public static void Set(IntPtr handle, object? value)
    {
        if (!_initialized || handle == IntPtr.Zero) return;

        int index = DecodeHandle(handle);
        if (index < 0 || index >= _capacity) return;

        ref HandleEntry entry = ref _entries[index];
        if (entry.Type == HandleType.Free) return;

        entry.Target = value != null ? Unsafe.AsPointer(ref value) : null;
    }

    /// <summary>
    /// Allocate a dependent handle with primary and secondary objects.
    /// </summary>
    /// <param name="primary">Primary object</param>
    /// <param name="secondary">Secondary (dependent) object</param>
    /// <returns>Handle value</returns>
    public static IntPtr AllocDependent(object? primary, object? secondary)
    {
        if (!_initialized)
        {
            return IntPtr.Zero;
        }

        // Future: _lock.Enter();
        try
        {
            if (_freeListHead == -1)
            {
                if (!Grow())
                {
                    return IntPtr.Zero;
                }
            }

            int index = _freeListHead;
            ref HandleEntry entry = ref _entries[index];

            _freeListHead = entry.NextFreeIndex;

            entry.Type = HandleType.Dependent;
            entry.Target = primary != null ? Unsafe.AsPointer(ref primary) : null;
            entry.Secondary = secondary != null ? Unsafe.AsPointer(ref secondary) : null;
            entry.NextFreeIndex = -1;

            _count++;

            return (IntPtr)((index + 1) << 2);
        }
        finally
        {
            // Future: _lock.Exit();
        }
    }

    /// <summary>
    /// Get both primary and secondary objects from a dependent handle.
    /// </summary>
    /// <param name="handle">Dependent handle</param>
    /// <param name="secondary">Output: secondary object</param>
    /// <returns>Primary object</returns>
    public static object? GetDependent(IntPtr handle, out object? secondary)
    {
        secondary = null;

        if (!_initialized || handle == IntPtr.Zero) return null;

        int index = DecodeHandle(handle);
        if (index < 0 || index >= _capacity) return null;

        ref HandleEntry entry = ref _entries[index];
        if (entry.Type != HandleType.Dependent) return null;

        if (entry.Secondary != null)
        {
            secondary = Unsafe.AsRef<object>(entry.Secondary);
        }

        if (entry.Target != null)
        {
            return Unsafe.AsRef<object>(entry.Target);
        }

        return null;
    }

    /// <summary>
    /// Set the secondary object of a dependent handle.
    /// </summary>
    /// <param name="handle">Dependent handle</param>
    /// <param name="secondary">New secondary object</param>
    public static void SetDependentSecondary(IntPtr handle, object? secondary)
    {
        if (!_initialized || handle == IntPtr.Zero) return;

        int index = DecodeHandle(handle);
        if (index < 0 || index >= _capacity) return;

        ref HandleEntry entry = ref _entries[index];
        if (entry.Type != HandleType.Dependent) return;

        entry.Secondary = secondary != null ? Unsafe.AsPointer(ref secondary) : null;
    }

    /// <summary>
    /// Get the current number of allocated handles.
    /// </summary>
    public static int Count => _count;

    /// <summary>
    /// Get the current capacity of the handle table.
    /// </summary>
    public static int Capacity => _capacity;

    /// <summary>
    /// Check if the handle table is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    #region GC Integration

    /// <summary>
    /// Delegate for marking objects during GC root enumeration.
    /// </summary>
    /// <param name="objPtr">Pointer to object to mark</param>
    public delegate void MarkObjectDelegate(void* objPtr);

    /// <summary>
    /// Enumerate all strong handles and call the marker delegate for each.
    /// This is used during GC mark phase to find roots.
    /// </summary>
    /// <param name="marker">Delegate to call for each strong reference</param>
    public static void EnumerateStrongHandles(MarkObjectDelegate marker)
    {
        if (!_initialized || marker == null) return;

        for (int i = 0; i < _capacity; i++)
        {
            ref HandleEntry entry = ref _entries[i];

            if (entry.Type == HandleType.Normal ||
                entry.Type == HandleType.Pinned ||
                entry.Type == HandleType.RefCounted)
            {
                if (entry.Target != null)
                {
                    marker(entry.Target);
                }
            }
            else if (entry.Type == HandleType.Dependent)
            {
                // Dependent handles: mark primary, and if primary is alive, mark secondary
                if (entry.Target != null)
                {
                    marker(entry.Target);
                }
                if (entry.Secondary != null)
                {
                    marker(entry.Secondary);
                }
            }
        }
    }

    /// <summary>
    /// Mark all strong handles (Normal, Pinned) as GC roots.
    /// Call this during GC mark phase.
    /// </summary>
    public static void MarkStrongHandles()
    {
        if (!_initialized) return;

        for (int i = 0; i < _capacity; i++)
        {
            ref HandleEntry entry = ref _entries[i];

            if (entry.Type == HandleType.Normal ||
                entry.Type == HandleType.Pinned ||
                entry.Type == HandleType.RefCounted)
            {
                // TODO: Mark entry.Target as a GC root
                // This will be called by the GC during root enumeration
            }
            else if (entry.Type == HandleType.Dependent && entry.Target != null)
            {
                // Dependent handles: if primary is alive, secondary should be kept alive
                // TODO: Mark both Target and Secondary if primary is marked
            }
        }
    }

    /// <summary>
    /// Clear weak handles whose targets have been collected.
    /// Call this after GC sweep phase.
    /// </summary>
    public static void ClearDeadWeakHandles()
    {
        if (!_initialized) return;

        for (int i = 0; i < _capacity; i++)
        {
            ref HandleEntry entry = ref _entries[i];

            if (entry.Type == HandleType.Weak ||
                entry.Type == HandleType.WeakTrackResurrection)
            {
                // TODO: Check if Target object is still alive
                // If not, set Target to null
                // For WeakTrackResurrection, wait until after finalization
            }
            else if (entry.Type == HandleType.Dependent)
            {
                // If primary is dead, clear both
                // TODO: Check if Target (primary) is alive
            }
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Decode handle value to entry index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeHandle(IntPtr handle)
    {
        // Handle format: ((index + 1) << 2)
        // So to get index: (handle >> 2) - 1
        return (int)((nint)handle >> 2) - 1;
    }

    /// <summary>
    /// Grow the handle table when full.
    /// </summary>
    private static bool Grow()
    {
        int newCapacity = _capacity * GrowthFactor;

        Serial.WriteString("[HandleTable] Growing from ");
        Serial.WriteNumber((ulong)_capacity);
        Serial.WriteString(" to ");
        Serial.WriteNumber((ulong)newCapacity);
        Serial.WriteString("\n");

        // Allocate new table
        int bytesNeeded = newCapacity * sizeof(HandleEntry);
        ulong pagesNeeded = ((ulong)bytesNeeded + PageAllocator.PageSize - 1) / PageAllocator.PageSize;

        HandleEntry* newEntries = (HandleEntry*)PageAllocator.AllocPages(PageType.Unmanaged, pagesNeeded, zero: true);

        if (newEntries == null)
        {
            return false;
        }

        // Copy existing entries
        for (int i = 0; i < _capacity; i++)
        {
            newEntries[i] = _entries[i];
        }

        // Initialize new free entries
        for (int i = _capacity; i < newCapacity - 1; i++)
        {
            newEntries[i].Type = HandleType.Free;
            newEntries[i].Target = null;
            newEntries[i].Secondary = null;
            newEntries[i].NextFreeIndex = i + 1;
        }
        newEntries[newCapacity - 1].Type = HandleType.Free;
        newEntries[newCapacity - 1].NextFreeIndex = -1;

        // Link new entries to free list
        // Find end of current free list and link to new entries
        if (_freeListHead == -1)
        {
            _freeListHead = _capacity;  // First new entry
        }
        else
        {
            // Walk to end of free list
            int current = _freeListHead;
            while (newEntries[current].NextFreeIndex != -1)
            {
                current = newEntries[current].NextFreeIndex;
            }
            newEntries[current].NextFreeIndex = _capacity;
        }

        // Free old table
        if (_entries != null)
        {
            PageAllocator.Free(_entries);
        }

        _entries = newEntries;
        _capacity = newCapacity;

        return true;
    }

    #endregion
}
