# ABA problem

Lock-free code updates shared state with compare-and-swap (`Interlocked.CompareExchange`): read the value, compute a new one, write it back only if the value is still what was read. The check has a blind spot: if another thread changed the value from A to B and back to A in between, the compare succeeds even though the state it described is gone. That is the ABA problem. For pointers and indexes into recyclable slots, "same value" does not mean "same meaning".

The standard fix is a version tag: pack a counter next to the value and bump it on every update, so a recycled value no longer compares equal.

In OrionGC the handle store uses this scheme: each handle segment packs its free-list head, alive count, and a version tag into one 64-bit word updated with `CompareExchange`, so a slot freed and reallocated between two reads cannot be mistaken for an unchanged free list. See [Handle store](../garbage-collector.md#handle-store).
