# Moving vs. non-moving

A moving collector relocates live objects during collection, either by copying them into a fresh region or by compacting them toward one end of the heap. Moving defeats fragmentation and keeps allocation trivially fast, but every reference to a moved object must be found and rewritten, which requires knowing precisely where every reference is.

A non-moving collector leaves every object at the address where it was allocated, for its whole lifetime. References never need fixing, object addresses can be handed to native code freely, and [conservative scanning](conservative-vs-precise.md) becomes possible. Fragmentation is the price, managed with free lists.

Conservative references and moving are incompatible: a conservatively found "reference" might really be an integer that happens to look like a heap address, so it cannot be rewritten, so its target may never move.

In OrionGC nothing ever moves, which is also why pinning costs nothing: a pinned object's address is as stable as any other object's. See the [Overview](../garbage-collector.md#overview).
