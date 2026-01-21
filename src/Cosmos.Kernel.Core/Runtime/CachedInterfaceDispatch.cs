// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime
{
    /// <summary>
    /// Interface dispatch cell - used for cached interface method dispatch
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct InterfaceDispatchCell
    {
        public nuint m_pStub;  // Pointer to dispatch stub function
        public nuint m_pCache; // Cache pointer (interface type or cached MethodTable)

        // Flags stored in low bits of m_pCache (from rhbinder.h)
        public const int IDC_CachePointerMask = 0x3;
        public const int IDC_CachePointerPointsAtCache = 0x0;
        public const int IDC_CachePointerIsInterfacePointerOrMetadataToken = 0x1;
        public const int IDC_CachePointerIsIndirectedInterfaceRelativePointer = 0x2;
        public const int IDC_CachePointerIsInterfaceRelativePointer = 0x3;
    }

    /// <summary>
    /// Information about a dispatch cell
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DispatchCellInfo
    {
        public DispatchCellType CellType;
        public MethodTable* InterfaceType;
        public ushort InterfaceSlot;
        public byte HasCache;
        public uint MetadataToken;
        public byte VTableOffset;
    }

    internal enum DispatchCellType : byte
    {
        InterfaceAndSlot = 0x0,
        MetadataToken = 0x1,
        VTableOffset = 0x2,
    }

    internal static unsafe class CachedInterfaceDispatch
    {
        /// <summary>
        /// Main entry point for interface dispatch resolution.
        /// Called from RhpInitialDynamicInterfaceDispatch to resolve interface method calls.
        /// </summary>
        [RuntimeExport("RhpCidResolve")]
        private static unsafe IntPtr RhpCidResolve(object pObject, IntPtr pCell)
        {
            Serial.WriteString("[CID] Start\n");

            if (pObject == null)
            {
                Serial.WriteString("[CID] Null\n");
                throw new NullReferenceException("Attempted to invoke interface method on null object");
            }

            // Get cell info without calling any complex methods
            RhpGetDispatchCellInfo(pCell, out DispatchCellInfo cellInfo);

            Serial.WriteString("[CID] Slot=");
            Serial.WriteHex((ushort)cellInfo.InterfaceSlot);
            Serial.WriteString("\n");

            // Resolve the dispatch
            IntPtr pTargetCode = RhResolveDispatchWorker(pObject, (void*)pCell, ref cellInfo);

            if (pTargetCode != IntPtr.Zero)
            {
                Serial.WriteString("[CID] OK\n");
                return pTargetCode;
            }

            Serial.WriteString("[CID] FAIL\n");
            throw new EntryPointNotFoundException("Could not find implementation for interface method");
        }

        [RuntimeExport("RhpResolveInterfaceMethod")]
        private static IntPtr RhpResolveInterfaceMethod(object pObject, IntPtr pCell)
        {
            if (pObject == null)
            {
                // Optimizer may perform code motion on dispatch such that it occurs independent of
                // null check on "this" pointer. Allow for this case by returning back an invalid pointer.
                return IntPtr.Zero;
            }

            MethodTable* pInstanceType = pObject.GetMethodTable();

            // This method is used for the implementation of LOAD_VIRT_FUNCTION and in that case the mapping we want
            // may already be in the cache.
            IntPtr pTargetCode = RhpSearchDispatchCellCache(pCell, pInstanceType);
            if (pTargetCode == IntPtr.Zero)
            {
                // Otherwise call the version of this method that knows how to resolve the method manually.
                pTargetCode = RhpCidResolve(pObject, pCell);
            }

            return pTargetCode;
        }

        [RuntimeExport("RhResolveDispatch")]
        private static IntPtr RhResolveDispatch(object pObject, MethodTable* interfaceType, ushort slot)
        {
            DispatchCellInfo cellInfo = default;
            cellInfo.CellType = DispatchCellType.InterfaceAndSlot;
            cellInfo.InterfaceType = interfaceType;
            cellInfo.InterfaceSlot = slot;

            return RhResolveDispatchWorker(pObject, null, ref cellInfo);
        }

        [RuntimeExport("RhResolveDispatchOnType")]
        private static IntPtr RhResolveDispatchOnType(MethodTable* pInstanceType, MethodTable* pInterfaceType, ushort slot)
        {
            return DispatchResolve.FindInterfaceMethodImplementationTarget(pInstanceType,
                                                                          pInterfaceType,
                                                                          slot,
                                                                          flags: default,
                                                                          ppGenericContext: null);
        }

        private static unsafe IntPtr RhResolveDispatchWorker(object pObject, void* cell, ref DispatchCellInfo cellInfo)
        {
            // Type of object we're dispatching on.
            MethodTable* pInstanceType = pObject.GetMethodTable();

            Serial.WriteString("[Resolve] MT=");
            Serial.WriteHex((ulong)pInstanceType);
            Serial.WriteString("\n");

            // Dump first 64 bytes of MethodTable to see its structure
            Serial.WriteString("[Resolve] MT dump: ");
            byte* mtBytes = (byte*)pInstanceType;
            for (int i = 0; i < 64; i++)
            {
                Serial.WriteHex(mtBytes[i]);
                if (i < 63) Serial.WriteString(" ");
            }
            Serial.WriteString("\n");

            // Check if the first field is a pointer to the real MethodTable
            MethodTable* possibleRealMT = *(MethodTable**)pInstanceType;
            if (possibleRealMT != null && possibleRealMT != pInstanceType)
            {
                Serial.WriteString("[Resolve] Possible canonical MT redirect to: ");
                Serial.WriteHex((ulong)possibleRealMT);
                Serial.WriteString("\n");

                Serial.WriteString("[Resolve] Real MT dump: ");
                byte* realMtBytes = (byte*)possibleRealMT;
                for (int i = 0; i < 64; i++)
                {
                    Serial.WriteHex(realMtBytes[i]);
                    if (i < 63) Serial.WriteString(" ");
                }
                Serial.WriteString("\n");

                // Try using the real MT
                pInstanceType = possibleRealMT;
            }

            if (cellInfo.CellType == DispatchCellType.InterfaceAndSlot)
            {
                Serial.WriteString("[Resolve] Itf=");
                Serial.WriteHex((ulong)cellInfo.InterfaceType);
                Serial.WriteString(" Slot=");
                Serial.WriteHex(cellInfo.InterfaceSlot);
                Serial.WriteString("\n");

                IntPtr pTargetCode = DispatchResolve.FindInterfaceMethodImplementationTarget(pInstanceType,
                                                                              cellInfo.InterfaceType,
                                                                              cellInfo.InterfaceSlot,
                                                                              flags: default,
                                                                              ppGenericContext: null);

                Serial.WriteString("[Resolve] Code=");
                Serial.WriteHex((ulong)pTargetCode);
                Serial.WriteString("\n");

                return pTargetCode;
            }
            else if (cellInfo.CellType == DispatchCellType.VTableOffset)
            {
                // Dereference VTable
                return *(IntPtr*)(((byte*)pInstanceType) + cellInfo.VTableOffset);
            }
            else
            {
                // MetadataToken dispatch not supported in Cosmos
                Serial.WriteString("[Resolve] BadType\n");
                throw new NotSupportedException("Metadata token dispatch not supported");
            }
        }

        /// <summary>
        /// Get information about a dispatch cell
        /// </summary>
        private static unsafe void RhpGetDispatchCellInfo(IntPtr pCell, out DispatchCellInfo cellInfo)
        {
            cellInfo = default;

            InterfaceDispatchCell* pDispatchCell = (InterfaceDispatchCell*)pCell;

            Serial.WriteString("[GetCellInfo] Cell=");
            Serial.WriteHex((ulong)pCell);
            Serial.WriteString("\n");

            // Extract the cache pointer and check flags
            nuint cachePointer = pDispatchCell[0].m_pCache;
            nuint stubPointer = pDispatchCell[0].m_pStub;

            Serial.WriteString("[GetCellInfo] Cell[0].Stub=");
            Serial.WriteHex((ulong)stubPointer);
            Serial.WriteString(" Cache=");
            Serial.WriteHex((ulong)cachePointer);
            Serial.WriteString("\n");

            Serial.WriteString("[GetCellInfo] Cell[1].Stub=");
            Serial.WriteHex((ulong)pDispatchCell[1].m_pStub);
            Serial.WriteString(" Cache=");
            Serial.WriteHex((ulong)pDispatchCell[1].m_pCache);
            Serial.WriteString("\n");

            int flags = (int)(cachePointer & InterfaceDispatchCell.IDC_CachePointerMask);

            Serial.WriteString("[GetCellInfo] Flags=");
            Serial.WriteHex((uint)flags);

            // Find the slot number - walk forward to find terminating cell (m_pStub == 0)
            InterfaceDispatchCell* currentCell = pDispatchCell;
            while (currentCell->m_pStub != 0)
            {
                currentCell++;
            }
            nuint cachePointerValueFlags = currentCell->m_pCache;

            Serial.WriteString(" TermCell.Cache=");
            Serial.WriteHex((ulong)cachePointerValueFlags);

            // Extract cell type and slot from terminating cell
            DispatchCellType cellType = (DispatchCellType)(cachePointerValueFlags >> 16);
            ushort interfaceSlot = (ushort)(cachePointerValueFlags & 0xFFFF);

            Serial.WriteString(" Type=");
            Serial.WriteHex((uint)cellType);
            Serial.WriteString(" Slot=");
            Serial.WriteHex(interfaceSlot);
            Serial.WriteString("\n");

            cellInfo.CellType = cellType;
            cellInfo.InterfaceSlot = interfaceSlot;

            if (flags == InterfaceDispatchCell.IDC_CachePointerIsInterfacePointerOrMetadataToken)
            {
                // Cell contains interface type pointer (direct)
                cellInfo.InterfaceType = (MethodTable*)(cachePointer & ~(nuint)InterfaceDispatchCell.IDC_CachePointerMask);
                cellInfo.HasCache = 0;
            }
            else if (flags == InterfaceDispatchCell.IDC_CachePointerPointsAtCache)
            {
                // Cell has been cached - contains MethodTable pointer (direct)
                cellInfo.InterfaceType = (MethodTable*)(cachePointer & ~(nuint)InterfaceDispatchCell.IDC_CachePointerMask);
                cellInfo.HasCache = 1;
            }
            else if (flags == InterfaceDispatchCell.IDC_CachePointerIsInterfaceRelativePointer ||
                     flags == InterfaceDispatchCell.IDC_CachePointerIsIndirectedInterfaceRelativePointer)
            {
                // From dotnet/runtime CachedInterfaceDispatch implementation:
                // UIntTarget interfacePointerValue = (UIntTarget)&m_pCache + (int32_t)cachePointerValue;
                // interfacePointerValue &= ~IDC_CachePointerMask;
                // cellInfo.InterfaceType = *(MethodTable**)interfacePointerValue;

                // Calculate address of m_pCache field (8 bytes after cell start)
                nuint cacheFieldAddress = (nuint)pCell + 8;

                // Cast cachePointer to signed int32 BEFORE masking, then add to field address
                int signedOffset = (int)cachePointer;
                nuint interfacePointerValue = cacheFieldAddress + (nuint)(nint)signedOffset;

                // NOW mask off the low bits
                interfacePointerValue &= ~(nuint)InterfaceDispatchCell.IDC_CachePointerMask;

                Serial.WriteString("[GetCellInfo] Indirected! CacheField=");
                Serial.WriteHex((ulong)cacheFieldAddress);
                Serial.WriteString(" SignedOff=");
                Serial.WriteHex((uint)signedOffset);
                Serial.WriteString(" PtrVal=");
                Serial.WriteHex((ulong)interfacePointerValue);

                // Dump memory around this location to see what's there
                byte* memPtr = (byte*)interfacePointerValue;
                Serial.WriteString(" Bytes[");
                for (int i = 0; i < 16; i++)
                {
                    Serial.WriteHex(memPtr[i]);
                    if (i < 15) Serial.WriteString(" ");
                }
                Serial.WriteString("]");

                // Read what's at this address
                ulong valueAt = *(ulong*)interfacePointerValue;
                uint valueAt32 = *(uint*)interfacePointerValue;

                Serial.WriteString(" [32]=");
                Serial.WriteHex(valueAt32);
                Serial.WriteString(" [64]=");
                Serial.WriteHex(valueAt);

                // Check which subcase: direct (0x3) or indirected (0x2)
                MethodTable* actualInterfaceType;
                if (flags == InterfaceDispatchCell.IDC_CachePointerIsInterfaceRelativePointer)
                {
                    // 0x3: The calculated address IS the MethodTable (direct)
                    actualInterfaceType = (MethodTable*)interfacePointerValue;
                    Serial.WriteString(" (direct)");
                }
                else
                {
                    // 0x2: The calculated address points to the MethodTable (indirected)
                    actualInterfaceType = *(MethodTable**)interfacePointerValue;
                    Serial.WriteString(" (indir)");
                }

                Serial.WriteString(" Final=");
                Serial.WriteHex((ulong)actualInterfaceType);
                Serial.WriteString("\n");

                cellInfo.InterfaceType = actualInterfaceType;
                cellInfo.HasCache = 0;
            }
            else
            {
                // Unknown format
                Serial.WriteString("[GetCellInfo] Unknown flags!\n");
                cellInfo.InterfaceType = (MethodTable*)(cachePointer & ~(nuint)InterfaceDispatchCell.IDC_CachePointerMask);
                cellInfo.HasCache = 0;
            }

            Serial.WriteString("[GetCellInfo] Result: Type=");
            Serial.WriteHex((ulong)cellInfo.InterfaceType);
            Serial.WriteString(" Slot=");
            Serial.WriteHex(cellInfo.InterfaceSlot);
            Serial.WriteString("\n");
        }

        /// <summary>
        /// Search the dispatch cell cache for a matching entry
        /// </summary>
        private static IntPtr RhpSearchDispatchCellCache(IntPtr pCell, MethodTable* pInstanceType)
        {
            // In a simple implementation, we don't maintain a separate cache
            // The dispatch cell itself serves as the cache after first resolution
            // For now, always return Zero to trigger resolution
            return IntPtr.Zero;
        }

        /// <summary>
        /// Create a new interface dispatch cell
        /// </summary>
        [RuntimeExport("RhNewInterfaceDispatchCell")]
        internal static IntPtr RhNewInterfaceDispatchCell(MethodTable* pInterface, int slotNumber)
        {
            // Allocate two cells (8 bytes * 2 = 16 bytes on 32-bit, 16 bytes * 2 = 32 bytes on 64-bit)
            InterfaceDispatchCell* pCell = (InterfaceDispatchCell*)
                SmallHeap.Alloc((uint)(sizeof(InterfaceDispatchCell) * 2));

            if (pCell == null)
                return IntPtr.Zero;

            // Initialize the dispatch cell
            // Cell[0].m_pStub would point to RhpInitialDynamicInterfaceDispatch in a full implementation
            // Cell[0].m_pCache contains the interface type pointer with flag bit set
            pCell[0].m_pStub = 0; // Would be address of RhpInitialDynamicInterfaceDispatch
            pCell[0].m_pCache = ((nuint)pInterface) | InterfaceDispatchCell.IDC_CachePointerIsInterfacePointerOrMetadataToken;

            // Cell[1] contains slot number
            pCell[1].m_pStub = 0;
            pCell[1].m_pCache = (nuint)slotNumber;

            return (IntPtr)pCell;
        }
    }
}
