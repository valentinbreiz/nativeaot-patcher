# GC roots

Reachability needs a starting point. The roots are the reference-holding locations that exist outside the heap and are treated as live by definition: local variables and saved registers on thread stacks, static fields, and handle tables through which native or runtime code keeps objects alive.

An object is live if and only if it can be reached from a root by following references. Everything else is garbage, even if other dead objects still point at it: a cycle of objects pointing at each other dies as a whole once no root reaches it.

In OrionGC the roots are the triggering thread's stack (scanned [precisely](conservative-vs-precise.md)), all other threads' stacks and registers (scanned conservatively), and the strong GC handles; static fields are reached through a strong handle rather than scanned directly. See [Mark phase](../garbage-collector.md#mark-phase).
