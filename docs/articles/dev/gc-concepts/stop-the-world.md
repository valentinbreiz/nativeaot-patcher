# Stop-the-world

A stop-the-world collector pauses every mutator (every thread running application code) for the full duration of a collection. Nothing allocates or writes references while the collector runs, so the object graph cannot change under it, and the collector needs no synchronization with running code.

The alternative family, concurrent and incremental collectors, lets threads keep running during parts of the collection. That removes the pause but demands heavy machinery: write barriers that tell the collector about references mutated mid-scan, and careful ordering so that a live object is never freed.

In OrionGC every collection is stop-the-world. It runs with interrupts disabled on the thread that triggered it, so no thread switch or interrupt handler can run until it finishes. See [Collection](../garbage-collector.md#collection).
