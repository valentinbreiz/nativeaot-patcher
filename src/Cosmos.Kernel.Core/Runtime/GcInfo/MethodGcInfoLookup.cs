// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Resolves an instruction pointer to the corresponding method's GCInfo blob. Parses the DWARF
// .eh_frame section to find the FDE (and its LSDA pointer into .dotnet_eh_table), then walks the
// NativeAOT LSDA header — mirrors UnixNativeCodeManager::FindMethodInfo + GetCodeOffset
// (dotnet/runtime/src/coreclr/nativeaot/Runtime/unix/UnixNativeCodeManager.cpp). See issue #346.
//
// NOTE: the .eh_frame walk here duplicates a slice of ExceptionHelper.ParseEhFrameForIP; the two
// are intended to be unified in a later phase (epic #348, phase 2).

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Runtime.GcInfo;

/// <summary>Maps an instruction pointer to its method's GCInfo blob and code offset.</summary>
public static unsafe partial class MethodGcInfoLookup
{
    // LSDA "unwind block" flags (UnixNativeCodeManager.cpp).
    private const byte UBF_FUNC_KIND_MASK = 0x03;
    private const byte UBF_FUNC_KIND_ROOT = 0x00;
    private const byte UBF_FUNC_KIND_FILTER = 0x02;
    private const byte UBF_FUNC_HAS_EHINFO = 0x04;
    private const byte UBF_FUNC_HAS_ASSOCIATED_DATA = 0x10;

    [LibraryImport("*", EntryPoint = "get_eh_frame_start")]
    [SuppressGCTransition]
    private static partial byte* GetEhFrameStart();

    [LibraryImport("*", EntryPoint = "get_eh_frame_end")]
    [SuppressGCTransition]
    private static partial byte* GetEhFrameEnd();

    /// <summary>Resolved GCInfo location for an instruction pointer.</summary>
    public struct MethodGcInfo
    {
        /// <summary>Pointer to the GCInfo blob (into .dotnet_eh_table).</summary>
        public byte* GcInfo;
        /// <summary>ip minus the main method's start address (the offset GCInfo is indexed by).</summary>
        public uint CodeOffset;
        /// <summary>FDE PC-begin for the (sub)function containing ip — the funclet start for a funclet.</summary>
        public nuint MethodStart;
        /// <summary>FDE PC-end for that (sub)function.</summary>
        public nuint MethodEnd;
        public bool IsFunclet;
        public bool IsFilter;
    }

    /// <summary>Resolve <paramref name="ip"/> to its method's GCInfo. Returns false if no managed FDE/LSDA covers it.</summary>
    public static bool TryGetMethodGcInfo(nuint ip, out MethodGcInfo info)
    {
        info = default;

        if (!TryGetFde(ip, out nuint funcStart, out nuint funcEnd, out byte* pLsda) || pLsda == null)
        {
            return false;
        }

        byte* p = pLsda;
        byte unwindBlockFlags = *p++;

        byte* pMainLsda;
        nuint methodStartAddress;
        if ((unwindBlockFlags & UBF_FUNC_KIND_MASK) != UBF_FUNC_KIND_ROOT)
        {
            // Funclet: refers to the main function's blob, with its own start-delta.
            pMainLsda = p + *(int*)p;
            p += sizeof(int);
            methodStartAddress = funcStart - (nuint)(nint)(*(int*)p);
            info.IsFunclet = true;
            info.IsFilter = (unwindBlockFlags & UBF_FUNC_KIND_MASK) == UBF_FUNC_KIND_FILTER;
        }
        else
        {
            pMainLsda = pLsda;
            methodStartAddress = funcStart;
        }

        // GetCodeOffset: skip optional associated-data / EH-info pointers at the main LSDA, then the GCInfo blob follows.
        byte* mp = pMainLsda;
        byte mainFlags = *mp++;
        if ((mainFlags & UBF_FUNC_HAS_ASSOCIATED_DATA) != 0)
        {
            mp += sizeof(int);
        }
        if ((mainFlags & UBF_FUNC_HAS_EHINFO) != 0)
        {
            mp += sizeof(int);
        }

        info.GcInfo = mp;
        info.CodeOffset = (uint)(ip - methodStartAddress);
        info.MethodStart = funcStart;
        info.MethodEnd = funcEnd;
        return true;
    }

    /// <summary>Find the FDE whose PC range contains <paramref name="ip"/>; report its range and LSDA pointer.</summary>
    private static bool TryGetFde(nuint ip, out nuint funcStart, out nuint funcEnd, out byte* pLsda)
    {
        funcStart = 0;
        funcEnd = 0;
        pLsda = null;

        byte* ehFrameStart = GetEhFrameStart();
        byte* ehFrameEnd = GetEhFrameEnd();
        if (ehFrameStart == null || ehFrameEnd == null || ehFrameStart >= ehFrameEnd)
        {
            return false;
        }

        byte* p = ehFrameStart;
        while (p < ehFrameEnd)
        {
            uint length = *(uint*)p;
            if (length == 0 || length == 0xFFFFFFFF)
            {
                break; // end of section / extended length not supported
            }
            byte* recordEnd = p + 4 + length;
            p += 4;

            uint ciePointer = *(uint*)p;
            p += 4;
            if (ciePointer == 0)
            {
                p = recordEnd; // a CIE — skip
                continue;
            }

            // FDE: PC-begin (sdata4, pc-relative), then PC-range (4 bytes).
            int pcBeginRel = *(int*)p;
            nuint pcBegin = (nuint)(p + pcBeginRel);
            p += 4;
            uint pcRange = *(uint*)p;
            p += 4;
            nuint pcEnd = pcBegin + pcRange;

            if (ip >= pcBegin && ip < pcEnd)
            {
                funcStart = pcBegin;
                funcEnd = pcEnd;

                uint augLen = ReadULEB128(ref p);
                if (augLen > 0)
                {
                    int lsdaRel = *(int*)p;
                    if (lsdaRel != 0)
                    {
                        pLsda = p + lsdaRel;
                    }
                }
                return pLsda != null;
            }
            p = recordEnd;
        }
        return false;
    }

    private static uint ReadULEB128(ref byte* p)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = *p++;
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);
        return result;
    }
}
