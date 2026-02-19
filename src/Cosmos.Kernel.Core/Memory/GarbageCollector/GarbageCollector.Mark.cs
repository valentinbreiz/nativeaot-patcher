// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

#pragma warning disable CS8500

/// <summary>
/// Mark phase: root scanning, reference enumeration, and mark stack management.
/// </summary>
public static unsafe partial class GarbageCollector
{
    /// <summary>
    /// Executes the mark phase: scans roots (stack, GC handles) and marks all reachable objects.
    /// </summary>
    private static void MarkPhase()
    {
        s_markStackCount = 0;
        ScanStackRoots();
        ScanGCHandles();
        //ScanStaticRoots();
    }

    /// <summary>
    /// Scans GC handle entries and marks objects referenced by strong handles (Normal and Pinned).
    /// Weak handles are skipped so their targets can be collected if otherwise unreachable.
    /// </summary>
    private static void ScanGCHandles()
    {
        if (s_handlerStore == null)
        {
            return;
        }

        int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);

        var handles = new Span<GCHandle>((void*)Align((uint)s_handlerStore->Bump), size);
        for (int i = 0; i < handles.Length; i++)
        {
            if ((IntPtr)handles[i].obj != IntPtr.Zero)
            {
                // Only mark objects for Normal and Pinned handles
                // Weak handles should not keep objects alive
                if (handles[i].type >= GCHandleType.Normal)
                {
                    TryMarkRoot((nint)handles[i].obj);
                }
            }
        }
    }

    /// <summary>
    /// Scans stack roots. When the scheduler is active, scans all thread stacks and saved registers;
    /// otherwise scans the current stack from RSP to the stack end.
    /// </summary>
    private static void ScanStackRoots()
    {
        if (CosmosFeatures.SchedulerEnabled && SchedulerManager.IsEnabled)
        {
            var cpuStates = SchedulerManager.GetAllCpuStates();
            if (cpuStates != null)
            {
                for (int i = 0; i < cpuStates.Length; i++)
                {
                    var state = cpuStates[i];
                    if (state?.CurrentThread != null)
                    {
                        ScanThreadStack(state.CurrentThread);
                    }
                }
            }
        }
        else
        {
            nuint rsp = ContextSwitch.GetRsp();
            nuint stackEnd = rsp + Scheduler.Thread.DefaultStackSize;
            ScanMemoryRange((nint*)rsp, (nint*)stackEnd);
        }
    }

    /// <summary>
    /// Scans a thread's saved register state and stack for potential object references.
    /// </summary>
    /// <param name="thread">The thread whose stack and registers to scan.</param>
    private static void ScanThreadStack(Scheduler.Thread thread)
    {
        if (thread == null)
        {
            return;
        }

        if (thread.State != Scheduler.ThreadState.Running)
        {
            Scheduler.ThreadContext* ctx = thread.GetContext();
            if (ctx != null)
            {
#if ARCH_ARM64
                // Scan all general-purpose registers X0-X30
                TryMarkRoot((nint)ctx->X0);
                TryMarkRoot((nint)ctx->X1);
                TryMarkRoot((nint)ctx->X2);
                TryMarkRoot((nint)ctx->X3);
                TryMarkRoot((nint)ctx->X4);
                TryMarkRoot((nint)ctx->X5);
                TryMarkRoot((nint)ctx->X6);
                TryMarkRoot((nint)ctx->X7);
                TryMarkRoot((nint)ctx->X8);
                TryMarkRoot((nint)ctx->X9);
                TryMarkRoot((nint)ctx->X10);
                TryMarkRoot((nint)ctx->X11);
                TryMarkRoot((nint)ctx->X12);
                TryMarkRoot((nint)ctx->X13);
                TryMarkRoot((nint)ctx->X14);
                TryMarkRoot((nint)ctx->X15);
                TryMarkRoot((nint)ctx->X16);
                TryMarkRoot((nint)ctx->X17);
                TryMarkRoot((nint)ctx->X18);
                TryMarkRoot((nint)ctx->X19);
                TryMarkRoot((nint)ctx->X20);
                TryMarkRoot((nint)ctx->X21);
                TryMarkRoot((nint)ctx->X22);
                TryMarkRoot((nint)ctx->X23);
                TryMarkRoot((nint)ctx->X24);
                TryMarkRoot((nint)ctx->X25);
                TryMarkRoot((nint)ctx->X26);
                TryMarkRoot((nint)ctx->X27);
                TryMarkRoot((nint)ctx->X28);
                TryMarkRoot((nint)ctx->X29);  // FP (Frame Pointer)
                TryMarkRoot((nint)ctx->X30);  // LR (Link Register)
                TryMarkRoot((nint)ctx->Sp);   // Stack Pointer
                TryMarkRoot((nint)ctx->Elr);  // Exception Link Register (return address)
#else
                // x64: Scan all general-purpose registers
                TryMarkRoot((nint)ctx->Rax);
                TryMarkRoot((nint)ctx->Rbx);
                TryMarkRoot((nint)ctx->Rcx);
                TryMarkRoot((nint)ctx->Rdx);
                TryMarkRoot((nint)ctx->Rsi);
                TryMarkRoot((nint)ctx->Rdi);
                TryMarkRoot((nint)ctx->Rbp);
                TryMarkRoot((nint)ctx->R8);
                TryMarkRoot((nint)ctx->R9);
                TryMarkRoot((nint)ctx->R10);
                TryMarkRoot((nint)ctx->R11);
                TryMarkRoot((nint)ctx->R12);
                TryMarkRoot((nint)ctx->R13);
                TryMarkRoot((nint)ctx->R14);
                TryMarkRoot((nint)ctx->R15);
#endif
            }
        }

        if (thread.StackBase != 0 && thread.StackSize != 0)
        {
            nuint stackStart = thread.StackPointer;
            nuint stackEnd = thread.StackBase + thread.StackSize;
            if (stackStart < stackEnd)
            {
                ScanMemoryRange((nint*)stackStart, (nint*)stackEnd);
            }
        }
    }

    /// <summary>
    /// Scans a contiguous memory range for potential object references (conservative scanning).
    /// </summary>
    /// <param name="start">Pointer to the first word to scan.</param>
    /// <param name="end">Pointer past the last word to scan.</param>
    private static void ScanMemoryRange(nint* start, nint* end)
    {
        for (nint* ptr = start; ptr < end; ptr++)
        {
            TryMarkRoot(*ptr);
        }
    }

    /// <summary>
    /// Attempts to mark a potential object reference. Validates that the pointer looks like a
    /// valid GC object (MethodTable outside heap) before marking and enumerating its references.
    /// Uses an iterative mark stack to avoid deep recursion.
    /// </summary>
    /// <param name="value">Potential object pointer to investigate.</param>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void TryMarkRoot(nint value)
    {
        PushMarkStack(value);

        while (s_markStackCount > 0)
        {
            nint ptr = PopMarkStack();
            var obj = (GCObject*)ptr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || IsInGCHeap((nint)mtPtr))
            {
                continue;
            }

            if (obj->IsMarked)
            {
                continue;
            }

            obj->Mark();

            MethodTable* mt = obj->GetMethodTable();
            if (mt->ContainsGCPointers)
            {
                EnumerateReferences(obj, mt);
            }
        }
    }

    /// <summary>
    /// Enumerates object references described by the GCDesc and pushes them onto the mark stack.
    /// Handles both fixed-layout objects (positive series count) and arrays of structs (negative series count).
    /// </summary>
    /// <param name="obj">The object whose references to enumerate.</param>
    /// <param name="mt">The object's MethodTable (must have <c>ContainsGCPointers</c> set).</param>
    private static void EnumerateReferences(GCObject* obj, MethodTable* mt)
    {
        nint numSeries = ((nint*)mt)[-1];
        if (numSeries == 0)
        {
            return;
        }

        var cur = (GCDescSeries*)((nint*)mt - 1) - 1;

        if (numSeries > 0)
        {
            uint objectSize = obj->ComputeSize();
            GCDescSeries* last = cur - numSeries + 1;

            do
            {
                nint size = cur->SeriesSize + (nint)objectSize;
                nint offset = cur->StartOffset;
                var ptr = (nint*)((nint)obj + offset);

                for (nint i = 0; i < size / IntPtr.Size; i++)
                {
                    nint refValue = ptr[i];
                    if (refValue != 0 && IsInGCHeap(refValue))
                    {
                        PushMarkStack(refValue);
                    }
                }

                cur--;
            } while (cur >= last);
        }
        else
        {
            nint offset = ((nint*)mt)[-2];
            var valSeries = (ValSerieItem*)((nint*)mt - 2) - 1;

            // Start at the offset
            var ptr = (nint*)((nint)obj + offset);

            // Retrieve the length of the array
            int length = obj->Length;

            // Repeat the loop for each element in the array
            for (int item = 0; item < length; item++)
            {
                for (int i = 0; i > numSeries; i--)
                {
                    // i is negative, so this is going backwards
                    ValSerieItem* valSerieItem = valSeries + i;

                    // Read valSerieItem->Nptrs pointers
                    for (int j = 0; j < valSerieItem->Nptrs; j++)
                    {
                        nint refValue = *ptr;
                        if (refValue != 0 && IsInGCHeap(refValue))
                        {
                            PushMarkStack(refValue);
                        }

                        ptr++;
                    }

                    // Skip valSerieItem->Skip bytes
                    ptr = (nint*)((nint)ptr + valSerieItem->Skip);
                }
            }
        }
    }

    /// <summary>
    /// Pushes a potential object pointer onto the mark stack. Expands the stack if full.
    /// </summary>
    /// <param name="ptr">The pointer to push.</param>
    private static void PushMarkStack(nint ptr)
    {
        if (s_markStackCount >= s_markStackCapacity)
        {
            // Expand mark stack
            ulong newPageCount = (s_markStackPageCount + 1) * 2;
            nint* newStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, newPageCount, true);
            if (newStack == null)
            {
                Serial.WriteString("[GC] WARNING: Mark stack overflow\n");
                return;
            }

            for (int i = 0; i < s_markStackCount; i++)
            {
                newStack[i] = s_markStack[i];
            }

            PageAllocator.Free(s_markStack);
            s_markStack = newStack;
            s_markStackCapacity = (int)(newPageCount * PageAllocator.PageSize / (ulong)sizeof(nint));
            s_markStackPageCount = newPageCount;
        }

        s_markStack[s_markStackCount++] = ptr;
    }

    /// <summary>
    /// Pops the top entry from the mark stack.
    /// </summary>
    /// <returns>The popped pointer value, or <c>0</c> if the stack is empty.</returns>
    private static nint PopMarkStack()
    {
        return s_markStackCount > 0 ? s_markStack[--s_markStackCount] : 0;
    }
}
