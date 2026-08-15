# Precise vs. conservative scanning

A stack scan must decide which words on a thread's stack are object references. There are two ways.

A precise scan knows. The compiler emits tables (GCInfo, in NativeAOT) describing, for each [safepoint](safepoint.md) in each method, exactly which stack slots and registers hold live references. The collector reports those slots and nothing else. Precise scanning never mistakes an integer for a pointer, and it is what makes moving collectors possible, but it only works at the code locations the tables describe.

A conservative scan guesses. Every pointer-sized word on the stack is treated as a potential reference, and anything that happens to point into the heap keeps its target alive. This works at any instruction with no compiler support, but has two costs: an integer that looks like a heap address retains garbage (a false root), and no conservatively referenced object may ever move, since the word cannot safely be rewritten.

OrionGC uses both. The thread that triggered the collection sits on a chain of managed call sites where GCInfo is valid, so it is scanned precisely; every other thread was preempted at an arbitrary instruction and is scanned conservatively. See [Precise Stack Scanning (GCInfo)](../garbage-collector-gcinfo.md).
