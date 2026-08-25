# Bump allocation

Bump allocation hands out memory by advancing a pointer: the allocator keeps a cursor into a free region, returns the current cursor for each request, and bumps it forward by the requested size. Allocation is one comparison and one addition, and successive allocations sit next to each other in memory, which is good for cache locality.

Its limitation is that it only works on untouched space. Once objects have been freed in the middle of a region, the holes are not behind the cursor anymore, so they must be recycled through free lists.

In OrionGC each segment has a `Bump` cursor for fresh space, threads bump privately inside their [TLABs](tlab.md), and the holes left by the sweep go to size-class free lists. See [Segments](../garbage-collector.md#segments) and [Free lists](../garbage-collector.md#free-lists).
