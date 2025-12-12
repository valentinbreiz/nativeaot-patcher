using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GC;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;

internal static partial class Program
{
    [LibraryImport("test", EntryPoint = "testGCC")]
    [return: MarshalUsing(typeof(SimpleStringMarshaler))]
    public static unsafe partial string testGCC();

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Serial.WriteString("[Main] Starting Main function\n");

        // GCC interop test (DevKernel-specific)
        Serial.WriteString("[Main] Testing GCC interop...\n");
        var gccString = testGCC();
        Serial.WriteString("[Main] SUCCESS - GCC string: ");
        Serial.WriteString(gccString);
        Serial.WriteString("\n");
        KernelConsole.Write("GCC interop: PASS - ");
        KernelConsole.WriteLine(gccString);

        DebugInfo.Print();

        // Print memory information before GC test
        MemoryOp.PrintMemoryInfo();

        // Test garbage collection
        TestGarbageCollection();

        // Print memory information after GC
        Serial.WriteString("\n[Main] Memory after GC:\n");
        MemoryOp.PrintMemoryInfo();

        Serial.WriteString("DevKernel: Changes to src/ will be reflected here!\n");

        // Register and test interrupt handler
#if ARCH_X64
        const int testInterrupt = 32;
#elif ARCH_ARM64
        const int testInterrupt = 0;  // SVC
#endif

        Serial.WriteString("[Main] Registering test interrupt handler...\n");
        InterruptManager.SetHandler(testInterrupt, TestInterruptHandler);
        Serial.WriteString("[Main] Handler registered!\n");

        Serial.WriteString("[Main] Triggering test interrupt...\n");
        TriggerTestInterrupt();
        Serial.WriteString("[Main] Test complete!\n");

        while (true) ;
    }

#if ARCH_X64
    [LibraryImport("*", EntryPoint = "_native_x64_test_int32")]
    private static partial void TriggerTestInterrupt();
#elif ARCH_ARM64
    [LibraryImport("*", EntryPoint = "_native_arm64_test_svc")]
    private static partial void TriggerTestInterrupt();
#endif

    // Handler for test interrupt - this will be called when the interrupt fires
    private static void TestInterruptHandler(ref IRQContext context)
    {
        Serial.WriteString("[Test Interrupt] Interrupt received!\n");
        Serial.WriteString("[Test Interrupt] Interrupt number: ");
        Serial.WriteString(context.interrupt.ToString());
        Serial.WriteString("\n");
        Serial.WriteString("[Test Interrupt] CPU Flags: 0x");
        Serial.WriteString(context.cpu_flags.ToString("X16"));
        Serial.WriteString("\n");
        Serial.WriteString("[Test Interrupt] Handler execution complete\n");
    }

    /// <summary>
    /// Test garbage collection with reference counting.
    /// </summary>
    private static void TestGarbageCollection()
    {
        Serial.WriteString("\n========== GC TEST (Reference Counting) ==========\n");

        // Get initial object count
        int initialCount = SmallHeap.GetAllocatedObjectCount();
        Serial.WriteString("[GC Test] Initial allocated objects: ");
        Serial.WriteNumber((ulong)initialCount);
        Serial.WriteString("\n");

        // Show reference counting stats
        Collector.PrintStats();

        // Create some temporary objects that should be collected
        Serial.WriteString("[GC Test] Creating temporary objects...\n");
        CreateTemporaryObjects();

        // Get count after allocation
        int afterAllocCount = SmallHeap.GetAllocatedObjectCount();
        Serial.WriteString("[GC Test] After allocation: ");
        Serial.WriteNumber((ulong)afterAllocCount);
        Serial.WriteString(" objects\n");

        // Show reference counting stats after allocation
        Collector.PrintStats();

        // Run garbage collection to sweep objects with refcount 0
        Serial.WriteString("[GC Test] Running Heap.Collect()...\n");
        int freed = Heap.Collect();

        Serial.WriteString("[GC Test] Freed ");
        Serial.WriteNumber((ulong)freed);
        Serial.WriteString(" objects\n");

        // Get count after GC
        int afterGcCount = SmallHeap.GetAllocatedObjectCount();
        Serial.WriteString("[GC Test] After GC: ");
        Serial.WriteNumber((ulong)afterGcCount);
        Serial.WriteString(" objects\n");

        // Final stats
        Collector.PrintStats();

        Serial.WriteString("========== GC TEST COMPLETE ==========\n\n");
    }

    /// <summary>
    /// Create some temporary objects that have no references and should be collected.
    /// With reference counting, these should have refcount decremented when they go out of scope.
    /// </summary>
    private static void CreateTemporaryObjects()
    {
        // These objects should become garbage after this method returns
        // With refcounting, their count should drop to 0 when references are overwritten

        // Create some strings (arrays of chars)
        for (int i = 0; i < 10; i++)
        {
            var temp = "Temporary string " + i.ToString();
            // temp goes out of scope - refcount should decrement
        }

        // Create some arrays
        for (int i = 0; i < 5; i++)
        {
            var tempArray = new int[100];
            // tempArray goes out of scope - refcount should decrement
        }
    }

    [ModuleInitializer]
    public static void Init()
    {
        Serial.WriteString("Kernel Init\n");
    }
}

[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(SimpleStringMarshaler))]
internal static unsafe class SimpleStringMarshaler
{
    public static string ConvertToManaged(char* unmanaged)
    {
        // Count the length of the null-terminated UTF-16 string
        int length = 0;
        char* p = unmanaged;
        while (*p != '\0')
        {
            length++;
            p++;
        }

        // Create a new string from the character span
        return new string(unmanaged, 0, length);
    }

    public static char* ConvertToUnmanaged(string managed)
    {
        fixed (char* p = managed)
        {
            return p;
        }
    }
}
