# Precise vs. conservative scanning

A stack scan must decide which words on a thread's stack are object references. There are two ways.

A precise scan knows. The compiler emits tables (GCInfo, in NativeAOT) describing, for each [safepoint](safepoint.md) in each method, exactly which stack slots and registers hold live references. The collector reports those slots and nothing else. Precise scanning never mistakes an integer for a pointer, and it is what makes moving collectors possible, but it only works at the code locations the tables describe.

A conservative scan guesses. Every pointer-sized word on the stack is treated as a potential reference, and anything that happens to point into the heap keeps its target alive. This works at any instruction with no compiler support, but has two costs: an integer that looks like a heap address retains garbage (a false root), and no conservatively referenced object may ever move, since the word cannot safely be rewritten.

Concretely, a thread's stack walked 8 bytes at a time:

| Stack word | What it actually is | Conservative verdict |
|------------|--------------------|--------------------|
| `0x0000000000000007` | int 7 | Not in heap range, ignored |
| `0xFFFF8001A2B3C4D0` | live `List<int>` reference | In heap range, marked ✅ correct |
| `0xFFFF8001DEADBEEF` | dead spill slot | Still in range, marked ❌ false root |
| `0xFFFFFFFF80123400` | a return address into kernel code | Kernel address, but outside heap segments, ignored |
| `0xFFFF8001CAFE0000` | stale callee pointer | Still in range, marked ❌ false root |

OrionGC uses both. The thread that triggered the collection sits on a chain of managed call sites where GCInfo is valid, so it is scanned precisely; every other thread was preempted at an arbitrary instruction and is scanned conservatively. See [Precise Stack Scanning (GCInfo)](../garbage-collector-gcinfo.md).
