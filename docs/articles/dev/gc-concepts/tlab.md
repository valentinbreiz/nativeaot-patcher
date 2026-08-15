# TLAB (thread-local allocation buffer)

With many threads allocating from one heap, a shared allocation cursor would need a lock or an atomic operation on every allocation. A TLAB removes that cost: each thread reserves a private chunk of the heap in one shared-state operation, then [bump-allocates](bump-allocation.md) inside it with no synchronization at all. Only when the chunk runs out does the thread go back to shared state for a refill.

The trade-off is waste: the unused tail of each thread's TLAB is memory no other thread can use until the TLAB is returned.

In OrionGC the TLAB is the `AllocContext` stored on each scheduler thread, the default TLAB size is 8 KiB, and every collection starts by returning all TLABs so the sweep sees a consistent heap. See [AllocContext](../garbage-collector.md#alloccontext-tlab), [TLAB refill](../garbage-collector.md#tlab-refill) and [Returning TLABs](../garbage-collector.md#returning-tlabs).
