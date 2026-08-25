# Runtime object header

In .NET every object carries a header word just before the object reference, at a negative offset, in addition to the `MethodTable` pointer at offset 0. The runtime uses it for bookkeeping that has no field of its own: the cached identity hash code (what `GetHashCode` returns for a plain object) and the thin lock that `Monitor.Enter` (the `lock` statement) takes on an uncontended object.

The GC never reads this word, but it must leave room for it: the runtime writes at `objRef - 4`, so writable bytes must exist before every object, including the very first object of a segment and any object that directly follows recycled free space.

In OrionGC that is why each segment reserves 8 bytes before its first object, and why every free block keeps its last 8 bytes out of allocation. This header is distinct from the [GCObject layout](../garbage-collector.md#object-header) that starts at the object reference itself. See [Segments](../garbage-collector.md#segments) and [Free lists](../garbage-collector.md#free-lists).
