// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.Core.Memory.VAS;

public readonly struct PageDirectoryAddress
{
    public readonly ulong Address;

    public PageDirectoryAddress(ulong address) => Address = address;
}

public readonly struct PageTableAddress
{
    public readonly ulong Address;

    public PageTableAddress(ulong address) => Address = address;
}

public readonly struct PageTableSegment
{
    public readonly PhysicalAddress PhysicalAddress;
    public readonly VirtualAddress VirtualAddress;
    public readonly PageFlags Flags;
    public readonly ulong PageCount;

    public PageTableSegment(PhysicalAddress physicalAddress, VirtualAddress virtualAddress, PageFlags flags, ulong pageCount)
    {
        PhysicalAddress = physicalAddress;
        VirtualAddress = virtualAddress;
        Flags = flags;
        PageCount = pageCount;
    }
}

[InlineArray(1024)]
public struct PageTableSegmentBuffer
{
    public PageTableSegment _element;
}

public readonly  struct PageTable
{
    public readonly PageTableAddress Root;
    public readonly PageTableSegmentBuffer Segments;
}

[InlineArray(1024)]
public struct PageTableBuffer
{
    public PageTable _element;
}

public readonly struct PageDirectory
{
    public readonly PageDirectoryAddress Root;
    public readonly PageTableBuffer Segments;
}
