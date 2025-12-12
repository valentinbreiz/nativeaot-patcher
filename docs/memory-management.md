# NativeAOT Kernel Memory Management

## Overview

This document describes how .NET objects are allocated and managed in the NativeAOT kernel using reference counting.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           .NET OBJECT ALLOCATION                            │
└─────────────────────────────────────────────────────────────────────────────┘

  C# Code: var obj = new MyClass();
                │
                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         NativeAOT Runtime Call                              │
│                                                                             │
│   RhpNewFast(MethodTable* pMT)          ◄── Regular objects                 │
│   RhpNewArray(MethodTable* pMT, int len) ◄── Arrays/Strings                 │
│                                                                             │
└───────────────────────────────┬─────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Memory.cs (Runtime)                                 │
│                                                                             │
│   1. Calculate size: pMT->RawBaseSize (+ length * ComponentSize for arrays) │
│   2. Call AllocObject(size) → MemoryOp.Alloc(size)                          │
│   3. Set MethodTable: *result = pMT                                         │
│   4. Set RefCount: RefCount.SetInitialRefCount(result, 1)  ◄── NEW!         │
│                                                                             │
└───────────────────────────────┬─────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MemoryOp.Alloc(size)                                │
│                                                                             │
│   Route to appropriate heap based on size:                                  │
│                                                                             │
│   ┌─────────────────┬─────────────────┬─────────────────┐                   │
│   │   SmallHeap     │   MediumHeap    │   LargeHeap     │                   │
│   │   ≤ 1020 bytes  │  1021-4072 bytes│   > 4072 bytes  │                   │
│   └────────┬────────┴────────┬────────┴────────┬────────┘                   │
│            │                 │                 │                            │
└────────────┼─────────────────┼─────────────────┼────────────────────────────┘
             │                 │                 │
             ▼                 ▼                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         PageAllocator (RAT)                                 │
│                                                                             │
│   RAM Allocation Table tracks page types:                                   │
│   ┌──────────────────────────────────────────────────────────────────┐      │
│   │ Page 0 │ Page 1 │ Page 2 │ Page 3 │ Page 4 │ ... │ Page N       │      │
│   │  RAT   │ Small  │ Small  │ Large  │ Large  │     │ Empty        │      │
│   └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│   PageTypes: Empty, HeapSmall, HeapMedium, HeapLarge, SMT, Unmanaged, etc.  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│                           OBJECT MEMORY LAYOUT                              │
└─────────────────────────────────────────────────────────────────────────────┘

SMALL HEAP OBJECT (≤ 1020 bytes):
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   ┌─────────────┬─────────────┬─────────────────────────────────────────┐   │
│   │ Size (u16)  │ RefCount(u16)│        Object Data                     │   │
│   │  2 bytes    │   2 bytes   │     (MethodTable* + fields)             │   │
│   └─────────────┴─────────────┴─────────────────────────────────────────┘   │
│   ◄── PrefixBytes (4) ──────►│◄────────── Actual Object ─────────────────►  │
│                               │                                             │
│   heapObject[0] = size        │  Object pointer returned to caller          │
│   heapObject[1] = refcount    │                                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

LARGE/MEDIUM HEAP OBJECT:
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   ┌───────────────────────────────────────┬─────────────────────────────┐   │
│   │         LargeHeapHeader               │      Object Data            │   │
│   │  ┌──────┬──────┬─────────┬─────────┐  │  (MethodTable* + fields)    │   │
│   │  │ Used │ Size │ Padding │ ObjectGc│  │                             │   │
│   │  │ (u64)│ (u32)│  (u32)  │         │  │                             │   │
│   │  │      │      │         │┌───────┐│  │                             │   │
│   │  │      │      │         ││Status ││  │                             │   │
│   │  │      │      │         ││Padding││  │                             │   │
│   │  │      │      │         ││RefCnt ││◄─┼── Reference count here      │   │
│   │  │      │      │         ││Reserved│  │                             │   │
│   │  │      │      │         │└───────┘│  │                             │   │
│   │  └──────┴──────┴─────────┴─────────┘  │                             │   │
│   └───────────────────────────────────────┴─────────────────────────────┘   │
│   ◄─────────── Header ───────────────────►│◄───── Object ──────────────►    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

.NET OBJECT STRUCTURE (after header):
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   ┌─────────────────┬─────────────────┬─────────────────────────────────┐   │
│   │  MethodTable*   │    [Length]     │         Fields / Elements       │   │
│   │    (8 bytes)    │  (arrays only)  │                                 │   │
│   └─────────────────┴─────────────────┴─────────────────────────────────┘   │
│                                                                             │
│   MethodTable contains:                                                     │
│   - Type information                                                        │
│   - BaseSize, ComponentSize                                                 │
│   - ContainsGCPointers flag                                                 │
│   - GCDesc (before MT) for reference field locations                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│                         REFERENCE COUNTING FLOW                             │
└─────────────────────────────────────────────────────────────────────────────┘

1. ALLOCATION (RefCount = 1)
   ─────────────────────────

   var obj = new MyClass();
        │
        ▼
   RhpNewFast() → AllocObject() → RefCount.SetInitialRefCount(obj, 1)

   Result: obj.RefCount = 1


2. ASSIGNMENT (IncRef new, DecRef old)
   ────────────────────────────────────

   myField = obj;  // Store reference
        │
        ▼
   RhpAssignRef(location, value)
        │
        ▼
   RefCount.AssignRef(location, value)
        │
        ├──► IncRef(newValue)     // newValue.RefCount++
        ├──► *location = newValue // Store pointer
        └──► DecRef(oldValue)     // oldValue.RefCount--
                    │
                    ▼
              If RefCount == 0 → FreeObjectAndReferences()


3. CASCADING FREE (DecRef children, then free)
   ────────────────────────────────────────────

   FreeObjectAndReferences(obj)
        │
        ├──► DecRefChildObjects(obj)
        │         │
        │         ├──► Get MethodTable
        │         ├──► If ContainsGCPointers:
        │         │         │
        │         │         ├──► Read GCDesc (before MT)
        │         │         └──► For each reference field:
        │         │                   DecRef(field) → may trigger more frees
        │         │
        │         └──► If Array of references:
        │                   For each element:
        │                       DecRef(element)
        │
        └──► SmallHeap.Free(obj) / LargeHeap.Free(obj)


┌─────────────────────────────────────────────────────────────────────────────┐
│                         GARBAGE COLLECTION                                  │
└─────────────────────────────────────────────────────────────────────────────┘

Heap.Collect() - Sweeps objects with RefCount = 0
────────────────────────────────────────────────

┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   Collector.Collect()                                                       │
│        │                                                                    │
│        ├──► SweepSmallHeap()                                                │
│        │         │                                                          │
│        │         └──► For each SMTPage → RootSMTBlock → SMTBlock:           │
│        │                   For each slot:                                   │
│        │                       If heapObject[0] != 0 (allocated)            │
│        │                       AND heapObject[1] == 0 (refcount = 0):       │
│        │                           SmallHeap.Free(slot)                     │
│        │                                                                    │
│        ├──► SweepLargeHeap()                                                │
│        │         │                                                          │
│        │         └──► For each page in RAT:                                 │
│        │                   If PageType == HeapMedium/HeapLarge:             │
│        │                       If header->Gc.RefCount == 0:                 │
│        │                           Heap.Free(obj)                           │
│        │                                                                    │
│        └──► SmallHeap.PruneSMT()                                            │
│                   │                                                         │
│                   └──► Free empty SMT pages back to PageAllocator           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│                         SMT (Size Map Table) STRUCTURE                      │
└─────────────────────────────────────────────────────────────────────────────┘

SMT organizes small heap allocations by size for fast allocation:

┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   SMTPage (linked list of pages)                                            │
│   ┌─────────┐     ┌─────────┐                                               │
│   │ SMTPage │────►│ SMTPage │────► NULL                                     │
│   │  .First │     │  .First │                                               │
│   └────┬────┘     └─────────┘                                               │
│        │                                                                    │
│        ▼                                                                    │
│   RootSMTBlock (sorted by size)                                             │
│   ┌───────────────┐     ┌───────────────┐     ┌───────────────┐             │
│   │ Size: 32      │────►│ Size: 64      │────►│ Size: 128     │────► ...    │
│   │ .First        │     │ .First        │     │ .First        │             │
│   └───────┬───────┘     └───────┬───────┘     └───────────────┘             │
│           │                     │                                           │
│           ▼                     ▼                                           │
│   SMTBlock (pages for this size)                                            │
│   ┌─────────────┐         ┌─────────────┐                                   │
│   │ PagePtr     │────►    │ PagePtr     │                                   │
│   │ SpacesLeft  │         │ SpacesLeft  │                                   │
│   │ NextBlock   │────►... │ NextBlock   │────► NULL                         │
│   └─────────────┘         └─────────────┘                                   │
│                                                                             │
│   Each page contains slots of fixed size:                                   │
│   ┌──────┬──────┬──────┬──────┬──────┬──────┐                               │
│   │Slot 0│Slot 1│Slot 2│Slot 3│ ...  │Slot N│  (4096 / slotSize slots)      │
│   └──────┴──────┴──────┴──────┴──────┴──────┘                               │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│                         GC HANDLE TABLE                                     │
└─────────────────────────────────────────────────────────────────────────────┘

For explicit GCHandle management (pinning, weak references):

┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   HandleTable (native memory, not on managed heap)                          │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  HandleEntry[0]  │  HandleEntry[1]  │  ...  │  HandleEntry[N]       │   │
│   │  ┌────────────┐  │  ┌────────────┐  │       │  ┌────────────┐       │   │
│   │  │ Target*    │  │  │ Target*    │  │       │  │ Target*    │       │   │
│   │  │ Secondary* │  │  │ Secondary* │  │       │  │ Secondary* │       │   │
│   │  │ Type       │  │  │ Type       │  │       │  │ Type       │       │   │
│   │  │ NextFree   │  │  │ NextFree   │  │       │  │ NextFree   │       │   │
│   │  └────────────┘  │  └────────────┘  │       │  └────────────┘       │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│   HandleTypes:                                                              │
│   - Weak: Does not prevent collection                                       │
│   - Normal: Prevents collection (strong reference)                          │
│   - Pinned: Prevents collection + movement                                  │
│   - Dependent: Secondary depends on primary                                 │
│                                                                             │
│   Free list for O(1) allocation:                                            │
│   FreeListHead → Entry[5] → Entry[2] → Entry[8] → -1                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│                         COMPLETE FLOW EXAMPLE                               │
└─────────────────────────────────────────────────────────────────────────────┘

Example: Creating and assigning objects

```csharp
class Container { public Data data; }
class Data { public int value; }

void Example()
{
    var container = new Container();  // (1)
    var data = new Data();            // (2)
    container.data = data;            // (3)
    container = null;                 // (4) - triggers cascade free
}
```

Step-by-step:

(1) new Container()
    ├─ RhpNewFast(Container.MT)
    ├─ Alloc 24 bytes (MT* + data field)
    ├─ Set container.RefCount = 1
    └─ Return pointer

(2) new Data()
    ├─ RhpNewFast(Data.MT)
    ├─ Alloc 16 bytes (MT* + int)
    ├─ Set data.RefCount = 1
    └─ Return pointer

(3) container.data = data
    ├─ RhpAssignRef(&container.data, data)
    ├─ RefCount.AssignRef():
    │   ├─ IncRef(data)        → data.RefCount = 2
    │   ├─ *location = data
    │   └─ DecRef(null)        → no-op
    └─ Result: data.RefCount = 2

(4) container = null (local variable overwritten/scope exit)
    ├─ RhpAssignRef(&container, null)
    ├─ RefCount.AssignRef():
    │   ├─ IncRef(null)        → no-op
    │   ├─ *location = null
    │   └─ DecRef(container)   → container.RefCount = 0
    │        │
    │        └─ FreeObjectAndReferences(container):
    │             ├─ DecRefChildObjects(container):
    │             │   └─ DecRef(container.data) → data.RefCount = 1
    │             └─ SmallHeap.Free(container)
    └─ Result: container freed, data.RefCount = 1

Note: data still has RefCount = 1 from step (2), would need explicit
      null assignment or scope exit to reach 0 and be freed.

┌─────────────────────────────────────────────────────────────────────────────┐
│                              KEY FILES                                      │
└─────────────────────────────────────────────────────────────────────────────┘

Memory Allocation:
- src/Cosmos.Kernel.Core/Runtime/Memory.cs         - RhpNewFast, RhpNewArray
- src/Cosmos.Kernel.Core/Memory/MemoryOp.cs        - Alloc(), Free()
- src/Cosmos.Kernel.Core/Memory/PageAllocator.cs   - Page management, RAT

Heap Implementation:
- src/Cosmos.Kernel.Core/Memory/Heap/Heap.cs       - Main heap API
- src/Cosmos.Kernel.Core/Memory/Heap/SmallHeap.cs  - Small objects (≤1020B)
- src/Cosmos.Kernel.Core/Memory/Heap/MediumHeap.cs - Medium objects
- src/Cosmos.Kernel.Core/Memory/Heap/LargeHeap.cs  - Large objects

GC / Reference Counting:
- src/Cosmos.Kernel.Core/Memory/GC/RefCount.cs     - IncRef, DecRef, AssignRef
- src/Cosmos.Kernel.Core/Memory/GC/Collector.cs    - Sweep RefCount=0 objects
- src/Cosmos.Kernel.Core/Memory/GC/HandleTable.cs  - GCHandle support

Headers:
- src/Cosmos.Kernel.Core/Memory/Heap/ObjectGc.cs   - Status, RefCount fields
- src/Cosmos.Kernel.Core/Memory/Heap/SmallHeapHeader.cs
- src/Cosmos.Kernel.Core/Memory/Heap/LargeHeapHeader.cs

Runtime Hooks:
- src/Cosmos.Kernel.Core/Runtime/Stdllib.cs        - RhpAssignRef → RefCount
