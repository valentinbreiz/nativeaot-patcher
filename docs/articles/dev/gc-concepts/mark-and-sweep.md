# Mark-and-sweep

Mark-and-sweep reclaims memory in two phases. The mark phase starts from the [roots](gc-roots.md) and follows references, setting a mark bit on every object it reaches; anything left unmarked is unreachable. The sweep phase then walks the heap linearly and turns every unmarked object into free space, leaving marked objects exactly where they are.

Its defining property is that live objects never move, which makes it the natural pairing for a [non-moving](non-moving.md) design. The price is fragmentation: freed holes stay holes, so the allocator must recycle them through free lists instead of getting one large contiguous region back, as copying or compacting collectors do.

In OrionGC the mark bit is bit 0 of the object's `MethodTable` pointer, and the sweep rebuilds the size-class free lists. See [Mark phase](../garbage-collector.md#mark-phase) and [Sweep phase](../garbage-collector.md#sweep-phase).
