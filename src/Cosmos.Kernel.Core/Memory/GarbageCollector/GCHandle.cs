// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Represents a single GC handle entry in the handle store.
/// </summary>
internal struct GCHandle
{
    public const GCHandleType FreeHandleType = (GCHandleType)(-1);

    /// <summary>
    /// Pointer to the managed object this handle references.
    /// </summary>
    public unsafe GCObject* Object;

    /// <summary>
    /// Additional info associated with this handle (e.g., weak track resurrection flag).
    /// </summary>
    public nint ExtraInfo;

    /// <summary>
    /// The type of this handle (Weak, Normal, Pinned, etc.).
    /// </summary>
    public GCHandleType Type;
}
