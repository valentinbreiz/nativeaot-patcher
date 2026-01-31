using System.Runtime.CompilerServices;
using Cosmos.Kernel.Boot.Limine;

namespace Cosmos.Kernel.Debug;

/// <summary>
/// Debug interface for memory/page allocator state.
/// Sends structured data over serial that can be parsed by debugging tools.
///
/// Protocol:
/// - Start: [COSMOS_MEM_BEGIN]
/// - Data: Key=Value pairs, one per line
/// - End: [COSMOS_MEM_END]
/// </summary>
public static unsafe class MemoryDebug
{
    // Protocol markers
    private const string BEGIN_MARKER = "[COSMOS_MEM_BEGIN]";
    private const string END_MARKER = "[COSMOS_MEM_END]";

    // Delegate for serial output (injected to avoid direct dependency)
    private static Action<string>? _writeString;
    private static Action<ulong>? _writeNumber;
    private static Action<ulong>? _writeHex;

    /// <summary>
    /// Initialize the memory debug interface with serial output functions.
    /// Call this from kernel initialization.
    /// </summary>
    public static void Initialize(
        Action<string> writeString,
        Action<ulong> writeNumber,
        Action<ulong> writeHex)
    {
        _writeString = writeString;
        _writeNumber = writeNumber;
        _writeHex = writeHex;
    }

    /// <summary>
    /// Send the current page allocator state over serial.
    /// </summary>
    /// <param name="ramStart">Start address of heap</param>
    /// <param name="heapEnd">End address of heap (where RAT begins)</param>
    /// <param name="ratLocation">Location of the RAT</param>
    /// <param name="ramSize">Total size of usable heap</param>
    /// <param name="totalPageCount">Total number of pages</param>
    /// <param name="freePageCount">Number of free pages</param>
    /// <param name="rat">Pointer to the RAT array</param>
    /// <param name="ratSampleCount">Number of RAT entries to send (0 = all, capped for performance)</param>
    public static void SendPageAllocatorState(
        byte* ramStart,
        byte* heapEnd,
        byte* ratLocation,
        ulong ramSize,
        ulong totalPageCount,
        ulong freePageCount,
        byte* rat,
        ulong ratSampleCount = 500)
    {
        if (_writeString == null || _writeNumber == null || _writeHex == null)
            return;

        _writeString(BEGIN_MARKER);
        _writeString("\n");

        // Page Allocator info
        WriteKeyValue("RamStart", (ulong)ramStart, hex: true);
        WriteKeyValue("HeapEnd", (ulong)heapEnd, hex: true);
        WriteKeyValue("RatLocation", (ulong)ratLocation, hex: true);
        WriteKeyValue("RamSize", ramSize, hex: false);
        WriteKeyValue("TotalPageCount", totalPageCount, hex: false);
        WriteKeyValue("FreePageCount", freePageCount, hex: false);

        // RAT sample - send page types for visualization
        // Limit to ratSampleCount to avoid flooding serial
        ulong sampleCount = ratSampleCount == 0 ? totalPageCount : ratSampleCount;
        if (sampleCount > totalPageCount)
            sampleCount = totalPageCount;

        WriteKeyValue("RatSampleCount", sampleCount, hex: false);

        // Send RAT data as comma-separated bytes
        _writeString("RatData=");
        for (ulong i = 0; i < sampleCount; i++)
        {
            if (i > 0)
                _writeString(",");
            _writeNumber!(rat[i]);
        }
        _writeString("\n");

        _writeString(END_MARKER);
        _writeString("\n");
    }

    /// <summary>
    /// Send a summary of page type counts.
    /// More efficient than sending raw RAT for large heaps.
    /// </summary>
    public static void SendPageTypeSummary(
        byte* ramStart,
        byte* heapEnd,
        byte* ratLocation,
        ulong ramSize,
        ulong totalPageCount,
        ulong freePageCount,
        byte* rat)
    {
        if (_writeString == null || _writeNumber == null || _writeHex == null)
            return;

        // Count pages by type
        ulong emptyCount = 0;
        ulong heapSmallCount = 0;
        ulong heapMediumCount = 0;
        ulong heapLargeCount = 0;
        ulong unmanagedCount = 0;
        ulong pageDirectoryCount = 0;
        ulong pageAllocatorCount = 0;
        ulong smtCount = 0;
        ulong extensionCount = 0;
        ulong unknownCount = 0;

        for (ulong i = 0; i < totalPageCount; i++)
        {
            byte pageType = rat[i];
            switch (pageType)
            {
                case 0: emptyCount++; break;      // Empty
                case 3: heapSmallCount++; break;  // HeapSmall
                case 5: heapMediumCount++; break; // HeapMedium
                case 7: heapLargeCount++; break;  // HeapLarge
                case 9: unmanagedCount++; break;  // Unmanaged
                case 11: pageDirectoryCount++; break; // PageDirectory
                case 32: pageAllocatorCount++; break; // PageAllocator (RAT)
                case 64: smtCount++; break;       // SMT
                case 128: extensionCount++; break; // Extension
                default: unknownCount++; break;
            }
        }

        _writeString(BEGIN_MARKER);
        _writeString("\n");

        // Basic info
        WriteKeyValue("RamStart", (ulong)ramStart, hex: true);
        WriteKeyValue("HeapEnd", (ulong)heapEnd, hex: true);
        WriteKeyValue("RatLocation", (ulong)ratLocation, hex: true);
        WriteKeyValue("RamSize", ramSize, hex: false);
        WriteKeyValue("TotalPageCount", totalPageCount, hex: false);
        WriteKeyValue("FreePageCount", freePageCount, hex: false);

        // Page type counts
        WriteKeyValue("EmptyPages", emptyCount, hex: false);
        WriteKeyValue("HeapSmallPages", heapSmallCount, hex: false);
        WriteKeyValue("HeapMediumPages", heapMediumCount, hex: false);
        WriteKeyValue("HeapLargePages", heapLargeCount, hex: false);
        WriteKeyValue("UnmanagedPages", unmanagedCount, hex: false);
        WriteKeyValue("PageDirectoryPages", pageDirectoryCount, hex: false);
        WriteKeyValue("PageAllocatorPages", pageAllocatorCount, hex: false);
        WriteKeyValue("SmtPages", smtCount, hex: false);
        WriteKeyValue("ExtensionPages", extensionCount, hex: false);
        WriteKeyValue("UnknownPages", unknownCount, hex: false);

        _writeString(END_MARKER);
        _writeString("\n");
    }

    /// <summary>
    /// Send complete memory state including Limine memory map and page allocator.
    /// </summary>
    public static void SendFullMemoryState(
        byte* ramStart,
        byte* heapEnd,
        byte* ratLocation,
        ulong ramSize,
        ulong totalPageCount,
        ulong freePageCount,
        byte* rat,
        ulong ratSampleCount = 500)
    {
        if (_writeString == null || _writeNumber == null || _writeHex == null)
            return;

        _writeString(BEGIN_MARKER);
        _writeString("\n");

        // Send Limine memory map
        SendLimineMemoryMapData();

        // Page Allocator info
        WriteKeyValue("RamStart", (ulong)ramStart, hex: true);
        WriteKeyValue("HeapEnd", (ulong)heapEnd, hex: true);
        WriteKeyValue("RatLocation", (ulong)ratLocation, hex: true);
        WriteKeyValue("RamSize", ramSize, hex: false);
        WriteKeyValue("TotalPageCount", totalPageCount, hex: false);
        WriteKeyValue("FreePageCount", freePageCount, hex: false);

        // RAT sample - send page types for visualization
        ulong sampleCount = ratSampleCount == 0 ? totalPageCount : ratSampleCount;
        if (sampleCount > totalPageCount)
            sampleCount = totalPageCount;

        WriteKeyValue("RatSampleCount", sampleCount, hex: false);

        // Send RAT data as comma-separated bytes
        _writeString("RatData=");
        for (ulong i = 0; i < sampleCount; i++)
        {
            if (i > 0)
                _writeString(",");
            _writeNumber!(rat[i]);
        }
        _writeString("\n");

        _writeString(END_MARKER);
        _writeString("\n");
    }

    /// <summary>
    /// Send Limine memory map entries.
    /// Format: LimineMemMap=count;base1,length1,type1;base2,length2,type2;...
    /// </summary>
    private static void SendLimineMemoryMapData()
    {
        var response = Limine.MemoryMap.Response;
        if (response == null)
        {
            WriteKeyValue("LimineMemMapCount", 0, hex: false);
            return;
        }

        var entryCount = response->EntryCount;
        WriteKeyValue("LimineMemMapCount", entryCount, hex: false);

        _writeString("LimineMemMap=");
        for (ulong i = 0; i < entryCount; i++)
        {
            var entry = response->Entries[i];
            if (i > 0)
                _writeString(";");

            // Format: base,length,type
            _writeString("0x");
            _writeHex!((ulong)entry->Base);
            _writeString(",");
            _writeNumber!(entry->Length);
            _writeString(",");
            _writeNumber!((ulong)entry->Type);
        }
        _writeString("\n");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteKeyValue(string key, ulong value, bool hex)
    {
        _writeString!(key);
        _writeString("=");
        if (hex)
        {
            _writeString("0x");
            _writeHex!(value);
        }
        else
        {
            _writeNumber!(value);
        }
        _writeString("\n");
    }
}
