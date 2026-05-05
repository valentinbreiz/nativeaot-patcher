using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.TestRunner.Framework;
using CoreGC = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

// Note: the enclosing namespace name "GarbageCollector" would shadow the core
// GarbageCollector type via parent-namespace lookup, so we alias it as CoreGC.
namespace Cosmos.Kernel.Tests.GarbageCollector;

public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[GarbageCollector] BeforeRun() reached!\n");
        Serial.WriteString("[GarbageCollector] Starting tests...\n");

        TR.Start("GarbageCollector Tests", expectedTests: 35);

        // Garbage Collection Tests
        TR.Run("GC_IsEnabled", TestGCIsEnabled);
        TR.Run("GC_GetStats", TestGCGetStats);
        TR.Run("GC_CollectBasic", TestGCCollectBasic);
        TR.Run("GC_StatsIncrement", TestGCStatsIncrement);
        TR.Run("GC_ExactCollectionCount", TestGCExactCollectionCount);
        TR.Run("GC_ObjectSurvival", TestGCObjectSurvival);
        TR.Run("GC_StringSurvival", TestGCStringSurvival);
        TR.Run("GC_ArraySurvival", TestGCArraySurvival);
        TR.Run("GC_ListSurvival", TestGCListSurvival);
        TR.Run("GC_UnreachableExactCount", TestGCUnreachableExactCount);
        TR.Run("GC_ObjectGraphSurvival", TestGCObjectGraphSurvival);
        TR.Run("GC_MixedTypeSurvival", TestGCMixedTypeSurvival);
        TR.Run("GC_AllocAfterCollect", TestGCAllocAfterCollect);
        TR.Run("GC_WeakReference", TestGCWeakReference);
        TR.Run("GC_LargeAllocCollect", TestGCLargeAllocationAndCollect);
        TR.Run("GC_StructArraySurvival", TestGCStructArraySurvival);
        TR.Run("GC_DictSurvival", TestGCDictionarySurvival);
        TR.Run("GC_PageAccounting", TestGCPageAccounting);
        TR.Run("GC_DependentHandle", TestGCDependentHandle);
        TR.Run("GC_DependentHandleCleanup", TestGCDependentHandleCleanup);
        TR.Run("GC_HandleStoreIntegrity", TestGCHandleStoreIntegrity);
        TR.Run("GC_PinnedHeapReuse", TestGCPinnedHeapReuse);

        // GC Info & Configuration Tests
        TR.Run("GC_Info_SimpleMemoryInfo", TestGCInfoSimpleMemoryInfo);
        TR.Run("GC_Info_HeapAndCommittedRelations", TestGCInfoHeapAndCommittedRelations);
        TR.Run("GC_Info_GenerationSizeAndFragmentation", TestGCInfoGenerationSizeAndFragmentation);
        TR.Run("GC_Info_LastGenInfoUpdated", TestGCInfoLastGenInfoUpdated);
        TR.Run("GC_Info_GetObjectGeneration", TestGCInfoGetObjectGeneration);
        TR.Run("GC_Info_GCSegmentSizeAndPercent", TestGCInfoGCSegmentSizeAndPercent);
        TR.Run("GC_Info_RhGetMemoryInfoWiring", TestGCInfoRhGetMemoryInfoWiring);
        TR.Run("GC_Variables", TestGCVariables);

        // TLAB (Thread-Local Allocation Buffer) Tests
        TR.Run("GC_TLAB_AllocBytesNonZero", TestTlabAllocBytesNonZero);
        TR.Run("GC_TLAB_AllocBytesIncrease", TestTlabAllocBytesIncrease);
        TR.Run("GC_TLAB_PreciseLessOrEqualTotal", TestTlabPreciseLessOrEqualTotal);
        TR.Run("GC_TLAB_SurvivalAfterCollect", TestTlabSurvivalAfterCollect);
        TR.Run("GC_TLAB_GapStampedOnCollect", TestTlabGapStampedOnCollect);

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run()
    {
        // All tests ran in BeforeRun; stop the main loop after one iteration
        Stop();
    }

    protected override void AfterRun()
    {
        // Flush coverage data and signal QEMU to terminate
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // ==================== Garbage Collection Tests ====================

    private static void TestGCIsEnabled()
    {
        Assert.True(CoreGC.IsEnabled, "GC: IsEnabled should be true");
    }

    private static void TestGCGetStats()
    {
        CoreGC.GetStats(out int totalCollections, out int totalObjectsFreed);
        // After running all previous tests, some collections may have already happened
        // (from allocation pressure). Both counters must be non-negative.
        Assert.True(totalCollections >= 0, "GC: totalCollections must be non-negative");
        Assert.True(totalObjectsFreed >= 0, "GC: totalObjectsFreed must be non-negative");
    }

    private static void TestGCCollectBasic()
    {
        // Snapshot before
        CoreGC.GetStats(out int collsBefore, out int freedBefore);

        int freed = CoreGC.Collect();
        Assert.True(freed >= 0, "GC: Collect must return non-negative freed count");

        // Verify stats incremented by exactly 1 collection
        CoreGC.GetStats(out int collsAfter, out int freedAfter);
        Assert.Equal(collsBefore + 1, collsAfter, "GC: Collect must increment collection count by exactly 1");
        Assert.Equal(freedBefore + freed, freedAfter, "GC: totalObjectsFreed must increase by exact freed count");
    }

    private static void TestGCStatsIncrement()
    {
        // Run 3 collections and verify exact increments
        CoreGC.GetStats(out int collsBefore, out int freedBefore);

        int freed1 = CoreGC.Collect();
        int freed2 = CoreGC.Collect();
        int freed3 = CoreGC.Collect();

        CoreGC.GetStats(out int collsAfter, out int freedAfter);

        // Exactly 3 collections must have been recorded
        Assert.Equal(collsBefore + 3, collsAfter, "GC: 3 successive Collects must increment count by exactly 3");

        // Total freed must be exact sum
        int expectedFreed = freedBefore + freed1 + freed2 + freed3;
        Assert.Equal(expectedFreed, freedAfter, "GC: totalObjectsFreed must equal sum of all Collect return values");
    }

    private static void TestGCExactCollectionCount()
    {
        // Verify that even a collection that frees nothing still increments the counter
        // First collect to clean up any existing garbage
        CoreGC.Collect();

        CoreGC.GetStats(out int collsBefore, out int freedBefore);

        // Second collect on a clean heap — likely frees 0 objects
        int freed = CoreGC.Collect();

        CoreGC.GetStats(out int collsAfter, out int freedAfter);

        // Collection count must always increment by 1, even if freed == 0
        Assert.Equal(collsBefore + 1, collsAfter, "GC: collection count increments even when freed == 0");
        Assert.Equal(freedBefore + freed, freedAfter, "GC: freed accounting exact even for zero-freed collection");
    }

    private static void TestGCObjectSurvival()
    {
        // Reachable boxed values must survive collection
        object boxed = 42;
        CoreGC.Collect();
        Assert.True((int)boxed == 42, "GC: boxed int survives collection");
    }

    private static void TestGCStringSurvival()
    {
        // Dynamically created strings must survive when reachable
        string s1 = "Hello";
        string s2 = "World";
        string concat = s1 + " " + s2;
        CoreGC.Collect();
        Assert.True(concat == "Hello World", "GC: concatenated string survives collection");
    }

    private static void TestGCArraySurvival()
    {
        // Arrays must survive collection when reachable
        int[] arr = new int[10];
        for (int i = 0; i < 10; i++)
        {
            arr[i] = i * 10;
        }

        CoreGC.Collect();
        Assert.Equal(10, arr.Length, "GC: array length survives collection");
        Assert.True(arr[0] == 0 && arr[5] == 50 && arr[9] == 90, "GC: array contents survive collection");
    }

    private static void TestGCListSurvival()
    {
        // List<T> with internal array must survive collection
        List<int> list = new List<int>();
        list.Add(100);
        list.Add(200);
        list.Add(300);
        CoreGC.Collect();
        Assert.Equal(3, list.Count, "GC: list count survives collection");
        Assert.True(list[0] == 100 && list[1] == 200 && list[2] == 300, "GC: list contents survive collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateExactUnreachable(int count, int arraySize)
    {
        // Each iteration creates exactly 1 unreachable byte[] object
        for (int i = 0; i < count; i++)
        {
            object obj = new byte[arraySize];
            // Prevent optimizer from removing the allocation
            if (obj == null)
            {
                break;
            }
        }
    }

    private static void TestGCUnreachableExactCount()
    {
        // Collect twice to stabilize — clear any prior garbage
        CoreGC.Collect();
        CoreGC.Collect();

        CoreGC.GetStats(out int _, out int freedBefore);

        // Allocate exactly 50 unreachable byte[64] arrays
        // Each byte[64]: BaseSize(24) + 64*1 = 88, Align(88) = 88
        const int objectCount = 50;
        AllocateExactUnreachable(objectCount, 64);

        // Collect — must free exactly those 50 objects (plus possibly some
        // internal allocations from the loop itself, e.g., enumerator objects)
        int freed = CoreGC.Collect();

        CoreGC.GetStats(out int _2, out int freedAfter);

        // freed must be at least objectCount - 2 (conservative scanning may
        // falsely retain a few objects when stack values coincidentally look like
        // GC heap pointers)
        Assert.True(freed >= objectCount - 2,
            "GC: must free at least " + (objectCount - 2) + " unreachable byte[64] objects, freed: " + freed);

        // freedAfter - freedBefore must match the Collect return value exactly
        Assert.Equal(freed, freedAfter - freedBefore,
            "GC: totalObjectsFreed delta must match Collect return value exactly");
    }

    private static void TestGCObjectGraphSurvival()
    {
        // Object graph: list holding strings must keep everything alive
        List<string> strings = new List<string>();
        strings.Add("Alpha");
        strings.Add("Beta");
        strings.Add("Gamma");

        CoreGC.Collect();

        Assert.Equal(3, strings.Count, "GC: object graph list count survives");
        Assert.True(strings[0] == "Alpha", "GC: object graph first element survives");
        Assert.True(strings[2] == "Gamma", "GC: object graph last element survives");
    }

    private static void TestGCMixedTypeSurvival()
    {
        // Various types allocated and kept alive across GC
        byte[] byteArr = new byte[] { 0xAA, 0xBB, 0xCC };
        int[] intArr = new int[] { 1, 2, 3 };
        string str = "MixedTest";
        object boxedLong = 9876543210L;
        TestPoint point = new TestPoint { X = 42, Y = 99 };
        object boxedPoint = point;

        CoreGC.Collect();

        Assert.True(byteArr[0] == 0xAA && byteArr[2] == 0xCC, "GC: byte array survives mixed collection");
        Assert.Equal(2, intArr[1], "GC: int array survives mixed collection");
        Assert.True(str == "MixedTest", "GC: string survives mixed collection");
        Assert.True((long)boxedLong == 9876543210L, "GC: boxed long survives mixed collection");
        TestPoint unboxed = (TestPoint)boxedPoint;
        Assert.True(unboxed.X == 42 && unboxed.Y == 99, "GC: boxed struct survives mixed collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateGarbage(int count, int size)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] temp = new byte[size];
            if (temp == null)
            {
                break;
            }
        }
    }

    private static void TestGCAllocAfterCollect()
    {
        // Generate garbage then collect, then allocate again
        AllocateGarbage(50, 256);

        CoreGC.GetStats(out int collsBefore, out int _);
        int freed = CoreGC.Collect();
        CoreGC.GetStats(out int collsAfter, out int _2);

        // Exactly 1 collection must have been recorded
        Assert.Equal(collsBefore + 1, collsAfter, "GC: exactly 1 collection after AllocateGarbage");
        // Must have freed at least 50 objects
        Assert.True(freed >= 50, "GC: must free at least 50 garbage byte[256] arrays, freed: " + freed);

        // Must be able to allocate after GC reclaims memory
        int[] newArr = new int[100];
        for (int i = 0; i < 100; i++)
        {
            newArr[i] = i;
        }

        Assert.Equal(99, newArr[99], "GC: allocation works after collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakRef()
    {
        object target = new byte[128];
        return new WeakReference(target);
    }

    private static void TestGCWeakReference()
    {
        // Create a weak reference to an object that becomes unreachable
        WeakReference weakRef = CreateWeakRef();

        // After collection, the weak handle's target must be cleared
        CoreGC.Collect();
        CoreGC.Collect(); // Second pass to ensure weak handles are freed

        Assert.True(!weakRef.IsAlive, "GC: weak reference cleared after collection");
        Assert.True(weakRef.Target == null, "GC: weak reference target is null after collection");
    }

    private static void TestGCLargeAllocationAndCollect()
    {
        // Allocate 20 × byte[4096], let them become garbage, then collect
        const int count = 20;
        AllocateGarbage(count, 4096);

        CoreGC.GetStats(out int _, out int freedBefore);
        int freed = CoreGC.Collect();
        CoreGC.GetStats(out int _2, out int freedAfter);

        // Must free at least the 20 large arrays
        Assert.True(freed >= count,
            "GC: must free at least " + count + " large byte[4096] arrays, freed: " + freed);
        Assert.Equal(freed, freedAfter - freedBefore,
            "GC: freedAfter - freedBefore must match freed count exactly");

        // Must be able to allocate again after large garbage is collected
        byte[] postGC = new byte[8192];
        postGC[0] = 0xDE;
        postGC[8191] = 0xAD;
        Assert.Equal((byte)0xDE, postGC[0], "GC: post-GC large alloc byte 0");
        Assert.Equal((byte)0xAD, postGC[8191], "GC: post-GC large alloc last byte");
    }

    private static void TestGCStructArraySurvival()
    {
        // Array of structs with value-type fields must survive GC
        TestPoint[] points = new TestPoint[5];
        for (int i = 0; i < 5; i++)
        {
            points[i] = new TestPoint { X = i * 10, Y = i * 20 };
        }

        CoreGC.Collect();

        Assert.Equal(5, points.Length, "GC: struct array length survives");
        Assert.Equal(0, points[0].X, "GC: struct array [0].X survives");
        Assert.Equal(0, points[0].Y, "GC: struct array [0].Y survives");
        Assert.Equal(40, points[4].X, "GC: struct array [4].X survives");
        Assert.Equal(80, points[4].Y, "GC: struct array [4].Y survives");
    }

    private static void TestGCDictionarySurvival()
    {
        // Dictionary with string keys and int values must survive GC
        Dictionary<string, int> dict = new Dictionary<string, int>();
        dict.Add("One", 1);
        dict.Add("Two", 2);
        dict.Add("Three", 3);

        CoreGC.Collect();

        Assert.Equal(3, dict.Count, "GC: dictionary count survives collection");
        Assert.Equal(1, dict["One"], "GC: dictionary value 'One' survives");
        Assert.Equal(3, dict["Three"], "GC: dictionary value 'Three' survives");
        Assert.True(dict.ContainsKey("Two"), "GC: dictionary ContainsKey after collection");
    }

    private static void TestGCPageAccounting()
    {
        // Verify PageAllocator accounting is consistent before and after GC
        ulong totalPages = PageAllocator.TotalPageCount;
        ulong freePagesBefore = PageAllocator.FreePageCount;
        ulong usedPagesBefore = totalPages - freePagesBefore;

        // Total pages must be positive
        Assert.True(totalPages > 0, "GC: TotalPageCount must be > 0");

        // Used pages must not exceed total
        Assert.True(usedPagesBefore <= totalPages,
            "GC: used pages (" + usedPagesBefore + ") must be <= total (" + totalPages + ")");

        // Allocate garbage to consume pages, then collect to reclaim
        AllocateGarbage(30, 4096);
        CoreGC.Collect();

        ulong freePagesAfter = PageAllocator.FreePageCount;

        // Total pages must stay constant (physical memory doesn't change)
        Assert.Equal((int)totalPages, (int)PageAllocator.TotalPageCount,
            "GC: TotalPageCount must not change after GC");

        // Free pages should stay the same or increase after GC (segments released)
        Assert.True(freePagesAfter >= freePagesBefore,
            "GC: free pages after collect (" + freePagesAfter + ") must be >= before (" + freePagesBefore + ")");
    }

    // ==================== Dependent Handle & Handle Store Tests ====================

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (object key, ConditionalWeakTable<object, byte[]> table) CreateDependentHandleScenario()
    {
        object key = new object();
        var table = new ConditionalWeakTable<object, byte[]>();
        table.AddOrUpdate(key, new byte[64]);
        return (key, table);
    }

    private static void TestGCDependentHandle()
    {
        // A ConditionalWeakTable keeps its values alive as long as the key is alive.
        // The value has NO direct reference from the key - only through the dependent handle.
        var (key, table) = CreateDependentHandleScenario();

        CoreGC.Collect();

        // The value should survive because key is still alive
        bool found = table.TryGetValue(key, out byte[] value);
        Assert.True(found, "GC: ConditionalWeakTable value must survive when key is alive");
        Assert.True(value != null && value.Length == 64, "GC: ConditionalWeakTable value data intact");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ConditionalWeakTable<object, byte[]> CreateOrphanedDepHandle()
    {
        var table = new ConditionalWeakTable<object, byte[]>();
        object key = new object();
        table.AddOrUpdate(key, new byte[128]);
        // key becomes unreachable after return
        return table;
    }

    private static void TestGCDependentHandleCleanup()
    {
        var table = CreateOrphanedDepHandle();

        CoreGC.Collect();
        CoreGC.Collect();

        // Table should be empty now - the key is dead, so the entry should be removed
        int count = 0;
        foreach (var kv in table)
        {
            count++;
        }

        Assert.Equal(0, count, "GC: ConditionalWeakTable entries cleared when key is dead");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference, WeakReference) CreateThreeWeakRefs()
    {
        // NoInlining ensures the byte[] pointers don't remain on the caller's stack
        return (
            new WeakReference(new byte[32]),
            new WeakReference(new byte[32]),
            new WeakReference(new byte[32])
        );
    }

    private static void TestGCHandleStoreIntegrity()
    {
        // Allocate handles via NoInlining helper to avoid conservative scan false positives
        var (wr1, wr2, wr3) = CreateThreeWeakRefs();

        // Force collection to exercise handle scanning
        CoreGC.Collect();
        CoreGC.Collect();

        // All weak refs should be cleared (targets are unreachable)
        Assert.True(!wr1.IsAlive, "GC: handle store wr1 cleared");
        Assert.True(!wr2.IsAlive, "GC: handle store wr2 cleared");
        Assert.True(!wr3.IsAlive, "GC: handle store wr3 cleared");

        // Allocate more handles to verify store still works
        object alive = new byte[64];
        WeakReference wr4 = new WeakReference(alive);
        CoreGC.Collect();
        Assert.True(wr4.IsAlive, "GC: handle store works after cleanup");
    }

    private static void TestGCPinnedHeapReuse()
    {
        // Verify pinned allocations don't crash after GC
        GC.AllocateArray<byte>(32, pinned: true);
        CoreGC.Collect();
        byte[] arr = GC.AllocateArray<byte>(32, pinned: true);
        Assert.True(arr != null, "GC: pinned allocation works after collection");
    }

    // ==================== GC Info & Configuration Tests ====================

    private static void TestGCInfoSimpleMemoryInfo()
    {
        // GetSimpleMemoryInfo() must report values that match each direct getter,
        // since it is built by calling them in sequence.
        CoreGC.SimpleMemoryInfo info = CoreGC.GetSimpleMemoryInfo();

        Assert.True(info.HeapSizeBytes == CoreGC.GetHeapSizeBytes(),
            "GC.Info: SimpleMemoryInfo.HeapSizeBytes must match GetHeapSizeBytes");
        Assert.True(info.FragmentedBytes == CoreGC.GetFragmentedBytes(),
            "GC.Info: SimpleMemoryInfo.FragmentedBytes must match GetFragmentedBytes");
        Assert.True(info.TotalCommittedBytes == CoreGC.GetTotalCommittedBytes(),
            "GC.Info: SimpleMemoryInfo.TotalCommittedBytes must match GetTotalCommittedBytes");
        Assert.True(info.MemoryLoadBytes == CoreGC.GetMemoryLoadBytes(),
            "GC.Info: SimpleMemoryInfo.MemoryLoadBytes must match GetMemoryLoadBytes");
        Assert.True(info.PinnedObjectsCount == CoreGC.GetPinnedObjectsCount(),
            "GC.Info: SimpleMemoryInfo.PinnedObjectsCount must match GetPinnedObjectsCount");

        // Non-generational GC: PromotedBytes is always 0 and CondemnedGeneration is always 0.
        Assert.True(info.PromotedBytes == 0UL,
            "GC.Info: PromotedBytes must be 0 (non-generational)");
        Assert.Equal(0, info.CondemnedGeneration,
            "GC.Info: CondemnedGeneration must be 0 (gen0 only)");

        // CollectionIndex must agree with GetCollectionIndex and the legacy GetStats counter.
        Assert.Equal(CoreGC.GetCollectionIndex(), info.CollectionIndex,
            "GC.Info: CollectionIndex must match GetCollectionIndex");
        CoreGC.GetStats(out int totalCollections, out int _);
        Assert.Equal(totalCollections, info.CollectionIndex,
            "GC.Info: CollectionIndex must equal GetStats totalCollections");
    }

    private static void TestGCInfoHeapAndCommittedRelations()
    {
        ulong heap = CoreGC.GetHeapSizeBytes();
        ulong committedSegs = CoreGC.GetCommittedGcSegmentsBytes();
        ulong totalCommitted = CoreGC.GetTotalCommittedBytes();

        // After dozens of allocation tests, both heap usage and segment commit must be non-zero.
        Assert.True(heap > 0UL, "GC.Info: heap size must be > 0 after prior tests");
        Assert.True(committedSegs > 0UL, "GC.Info: committed GC segments must be > 0");

        // Heap usage cannot exceed committed segment capacity.
        Assert.True(heap <= committedSegs,
            "GC.Info: heap size must be <= committed GC segments");

        // Total committed includes regular segments plus pinned/frozen/handler/etc.
        Assert.True(totalCommitted >= committedSegs,
            "GC.Info: total committed must be >= committed GC segments");

        // MemoryLoadBytes is page-based and must be > 0 (the kernel itself uses pages).
        Assert.True(CoreGC.GetMemoryLoadBytes() > 0UL,
            "GC.Info: memory load must be > 0");
    }

    private static void TestGCInfoGenerationSizeAndFragmentation()
    {
        // Gen 0 size covers regular segments only; heap size includes pinned segments too.
        ulong gen0Size = CoreGC.GetGenerationSize(0);
        ulong heapSize = CoreGC.GetHeapSizeBytes();
        Assert.True(gen0Size > 0UL, "GC.Info: gen0 size must be > 0");
        Assert.True(gen0Size <= heapSize,
            "GC.Info: GetGenerationSize(0) must be <= GetHeapSizeBytes (heap includes pinned)");

        // Non-generational GC: any non-zero generation reports 0.
        Assert.Equal(0UL, CoreGC.GetGenerationSize(1),
            "GC.Info: gen1 size must be 0");
        Assert.Equal(0UL, CoreGC.GetGenerationSize(2),
            "GC.Info: gen2 size must be 0");
        Assert.Equal(0UL, CoreGC.GetGenerationSize(-1),
            "GC.Info: invalid generation index must return 0");

        // GetCurrentFragmentation(0) must agree with GetFragmentedBytes; other gens return 0.
        Assert.True(CoreGC.GetCurrentFragmentation(0) == CoreGC.GetFragmentedBytes(),
            "GC.Info: GetCurrentFragmentation(0) must match GetFragmentedBytes");
        Assert.True(CoreGC.GetCurrentFragmentation(1) == 0UL,
            "GC.Info: gen1 fragmentation must be 0");
        Assert.True(CoreGC.GetCurrentFragmentation(2) == 0UL,
            "GC.Info: gen2 fragmentation must be 0");
    }

    private static void TestGCInfoLastGenInfoUpdated()
    {
        // Allocate garbage so the gen0 snapshot has meaningful data after the next collect.
        AllocateGarbage(40, 256);

        CoreGC.Collect();

        // After collect, gen0 last-snapshot must reflect the live heap state.
        ulong sizeBefore = CoreGC.GetLastGenSizeBefore(0);
        ulong sizeAfter = CoreGC.GetLastGenSizeAfter(0);

        Assert.True(sizeBefore > 0UL,
            "GC.Info: GetLastGenSizeBefore(0) must be > 0 after a collection on a non-empty heap");
        Assert.True(sizeAfter > 0UL,
            "GC.Info: GetLastGenSizeAfter(0) must be > 0");

        // Non-generational GC: any non-zero generation must return 0 for all four accessors.
        Assert.True(CoreGC.GetLastGenSizeBefore(1) == 0UL,
            "GC.Info: gen1 LastGenSizeBefore must be 0");
        Assert.True(CoreGC.GetLastGenSizeAfter(1) == 0UL,
            "GC.Info: gen1 LastGenSizeAfter must be 0");
        Assert.True(CoreGC.GetLastGenFragmentationBefore(1) == 0UL,
            "GC.Info: gen1 LastGenFragmentationBefore must be 0");
        Assert.True(CoreGC.GetLastGenFragmentationAfter(1) == 0UL,
            "GC.Info: gen1 LastGenFragmentationAfter must be 0");
    }

    private static void TestGCInfoGetObjectGeneration()
    {
        // Null pointer is the explicit sentinel for "not in any GC generation".
        Assert.Equal(int.MaxValue, CoreGC.GetObjectGeneration((nint)0),
            "GC.Info: null pointer must return int.MaxValue");

        // A live managed allocation must report generation 0.
        object alive = new byte[64];
        nint aliveAddr = Unsafe.As<object, nint>(ref alive);
        Assert.Equal(0, CoreGC.GetObjectGeneration(aliveAddr),
            "GC.Info: live managed object must report gen 0");

        // Keep the object reachable so the address stays valid for the duration of the test.
        Assert.NotNull(alive, "GC.Info: alive sentinel must not be null");
    }

    private static void TestGCInfoGCSegmentSizeAndPercent()
    {
        ulong segSize = CoreGC.GetGCSegmentSizeBytes();
        Assert.True(segSize > 0UL, "GC.Info: GC segment size must be > 0");

        int pct = CoreGC.GetLastGCPercentTimeInGC();
        Assert.True(pct >= 0 && pct <= 100,
            "GC.Info: GetLastGCPercentTimeInGC must be in [0, 100]");
    }

    private static void TestGCInfoRhGetMemoryInfoWiring()
    {
        // System.GC.GetGCMemoryInfo() routes through RhGetMemoryInfo, which fills a
        // GCMemoryInfoData struct from CoreGC.GetSimpleMemoryInfo() plus the
        // per-generation last-collect snapshots. The struct layout must match exactly.
        //
        // Order matters: GC.GetGCMemoryInfo() allocates a GCMemoryInfoData class instance
        // before populating it, which can consume a free-list block and shift FragmentedBytes.
        // Read the runtime snapshot first, then take the direct snapshot — both then reflect
        // the post-allocation heap state and must agree.
        GCMemoryInfo runtimeInfo = GC.GetGCMemoryInfo();
        CoreGC.SimpleMemoryInfo direct = CoreGC.GetSimpleMemoryInfo();

        Assert.Equal((long)direct.HeapSizeBytes, runtimeInfo.HeapSizeBytes,
            "GC.Info: GCMemoryInfo.HeapSizeBytes must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.FragmentedBytes, runtimeInfo.FragmentedBytes,
            "GC.Info: GCMemoryInfo.FragmentedBytes must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.TotalCommittedBytes, runtimeInfo.TotalCommittedBytes,
            "GC.Info: GCMemoryInfo.TotalCommittedBytes must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.MemoryLoadBytes, runtimeInfo.MemoryLoadBytes,
            "GC.Info: GCMemoryInfo.MemoryLoadBytes must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.PromotedBytes, runtimeInfo.PromotedBytes,
            "GC.Info: GCMemoryInfo.PromotedBytes must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.PinnedObjectsCount, runtimeInfo.PinnedObjectsCount,
            "GC.Info: GCMemoryInfo.PinnedObjectsCount must mirror GetSimpleMemoryInfo");
        Assert.Equal((long)direct.CollectionIndex, runtimeInfo.Index,
            "GC.Info: GCMemoryInfo.Index must equal CollectionIndex");
        Assert.Equal(direct.CondemnedGeneration, runtimeInfo.Generation,
            "GC.Info: GCMemoryInfo.Generation must equal CondemnedGeneration");

        // RamSize is the total available memory; the high-load threshold is half of it.
        Assert.Equal((long)PageAllocator.RamSize, runtimeInfo.TotalAvailableMemoryBytes,
            "GC.Info: TotalAvailableMemoryBytes must equal PageAllocator.RamSize");
        Assert.Equal((long)(PageAllocator.RamSize / 2), runtimeInfo.HighMemoryLoadThresholdBytes,
            "GC.Info: HighMemoryLoadThresholdBytes must equal RamSize / 2");

        // Generation 0 last-collect snapshot must mirror CoreGC.GetLastGen* getters.
        Assert.Equal((long)CoreGC.GetLastGenSizeBefore(0), runtimeInfo.GenerationInfo[0].SizeBeforeBytes,
            "GC.Info: GenerationInfo[0].SizeBeforeBytes must mirror GetLastGenSizeBefore(0)");
        Assert.Equal((long)CoreGC.GetLastGenSizeAfter(0), runtimeInfo.GenerationInfo[0].SizeAfterBytes,
            "GC.Info: GenerationInfo[0].SizeAfterBytes must mirror GetLastGenSizeAfter(0)");
        Assert.Equal((long)CoreGC.GetLastGenFragmentationBefore(0), runtimeInfo.GenerationInfo[0].FragmentationBeforeBytes,
            "GC.Info: GenerationInfo[0].FragmentationBeforeBytes must mirror GetLastGenFragmentationBefore(0)");
        Assert.Equal((long)CoreGC.GetLastGenFragmentationAfter(0), runtimeInfo.GenerationInfo[0].FragmentationAfterBytes,
            "GC.Info: GenerationInfo[0].FragmentationAfterBytes must mirror GetLastGenFragmentationAfter(0)");
    }

    private static void TestGCVariables()
    {
        IReadOnlyDictionary<string, object> vars = CoreGC.Variables;
        Assert.NotNull(vars, "GC.Info: Variables must be non-null after GC initialization");

        // Standalone GC identification.
        Assert.Equal("OrionGC", (string)vars["GCName"],
            "GC.Info: GCName must be \"OrionGC\"");
        Assert.Equal("", (string)vars["GCPath"],
            "GC.Info: GCPath must be empty string");

        // All boolean GC mode flags default to false in this kernel.
        Assert.Equal(false, (bool)vars["gcServer"],
            "GC.Info: gcServer must be false");
        Assert.Equal(false, (bool)vars["gcConcurrent"],
            "GC.Info: gcConcurrent must be false");
        Assert.Equal(false, (bool)vars["GCRetainVM"],
            "GC.Info: GCRetainVM must be false");
        Assert.Equal(false, (bool)vars["GCNoAffinitize"],
            "GC.Info: GCNoAffinitize must be false");
        Assert.Equal(false, (bool)vars["GCCpuGroup"],
            "GC.Info: GCCpuGroup must be false");
        Assert.Equal(false, (bool)vars["GCLargePages"],
            "GC.Info: GCLargePages must be false");
    }

    // ==================== TLAB (Thread-Local Allocation Buffer) Tests ====================

    private static void TestTlabAllocBytesNonZero()
    {
        // After all previous tests, per-thread cumulative allocated bytes must be > 0
        long threadBytes = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(threadBytes > 0,
            "TLAB: GetAllocatedBytesForCurrentThread must be > 0 after allocations, got: " + threadBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AllocByteArray(int size)
    {
        return new byte[size];
    }

    private static void TestTlabAllocBytesIncrease()
    {
        // Allocating objects must increase the global total allocated bytes counter
        long before = GC.GetTotalAllocatedBytes(precise: false);

        // Allocate via NoInlining helper to prevent compiler elision
        byte[] a1 = AllocByteArray(128);
        byte[] a2 = AllocByteArray(256);
        byte[] a3 = AllocByteArray(512);

        long after = GC.GetTotalAllocatedBytes(precise: false);

        // Keep references alive so they're not optimized away
        Assert.True(a1 != null && a2 != null && a3 != null, "TLAB: allocations must not be null");
        Assert.True(after > before,
            "TLAB: GetTotalAllocatedBytes must increase after allocations");
    }

    private static void TestTlabPreciseLessOrEqualTotal()
    {
        // Precise total subtracts unused TLAB space; must be <= non-precise total
        long total = GC.GetTotalAllocatedBytes(precise: false);
        long precise = GC.GetTotalAllocatedBytes(precise: true);
        Assert.True(total > 0, "TLAB: GetTotalAllocatedBytes must be > 0");
        Assert.True(precise > 0, "TLAB: GetTotalAllocatedBytes(precise) must be > 0");
        Assert.True(precise <= total,
            "TLAB: precise (" + precise + ") must be <= non-precise (" + total + ")");
    }

    private static void TestTlabSurvivalAfterCollect()
    {
        // Allocate objects, trigger GC (which returns all TLABs), then verify
        // objects survive and we can allocate again (TLAB refill works)
        List<int> list = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            list.Add(i);
        }

        CoreGC.Collect();

        // List must survive
        Assert.Equal(100, list.Count, "TLAB: list count must survive GC");
        Assert.Equal(99, list[99], "TLAB: list last element must survive GC");

        // Post-GC allocation must work (TLAB refill after ReturnAllAllocContexts)
        byte[] postGC = new byte[256];
        postGC[0] = 0xAB;
        Assert.Equal((byte)0xAB, postGC[0], "TLAB: allocation after GC must work");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateTlabGarbage(int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] temp = new byte[64];
            if (temp == null)
            {
                break;
            }
        }
    }

    private static void TestTlabGapStampedOnCollect()
    {
        // After GC, unused TLAB gaps are stamped as FreeBlocks.
        // Verify that a collection doesn't corrupt the heap by allocating
        // garbage, collecting, then allocating more and verifying correctness.
        AllocateTlabGarbage(100);

        CoreGC.GetStats(out int collsBefore, out int _);
        int freed = CoreGC.Collect();
        CoreGC.GetStats(out int collsAfter, out int _2);

        Assert.Equal(collsBefore + 1, collsAfter,
            "TLAB: exactly 1 collection recorded");
        Assert.True(freed >= 0,
            "TLAB: freed count must be >= 0");

        // Allocate after collect to verify heap integrity (TLAB gaps properly handled)
        int[] arr = new int[50];
        for (int i = 0; i < 50; i++)
        {
            arr[i] = i * 3;
        }

        Assert.Equal(147, arr[49], "TLAB: post-collect allocation data integrity");
    }
}

// Test struct for boxing and collection tests
internal struct TestPoint
{
    public int X;
    public int Y;
}
