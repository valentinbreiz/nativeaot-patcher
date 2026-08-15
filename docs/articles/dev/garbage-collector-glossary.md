# Garbage Collector Glossary

Short background notes on the general GC concepts the [Garbage Collector](garbage-collector.md) article builds on. Each page explains the concept on its own, then closes with one line on how OrionGC applies it.

| Concept | Summary |
|---------|---------|
| [Stop-the-world](gc-concepts/stop-the-world.md) | All threads pause for the full duration of a collection |
| [Mark-and-sweep](gc-concepts/mark-and-sweep.md) | Mark what is reachable, sweep the rest in place |
| [Moving vs. non-moving](gc-concepts/non-moving.md) | Whether live objects can ever be relocated |
| [Generations](gc-concepts/gc-generations.md) | Age-segregated heaps, and what a single generation avoids |
| [Bump allocation](gc-concepts/bump-allocation.md) | Allocation by advancing a pointer through fresh space |
| [TLAB](gc-concepts/tlab.md) | Per-thread buffers that make the allocation fast path synchronization-free |
| [GC roots](gc-concepts/gc-roots.md) | Where reachability starts |
| [Precise vs. conservative scanning](gc-concepts/conservative-vs-precise.md) | Knowing which stack words are references, or guessing |
