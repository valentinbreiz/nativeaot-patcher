using System.Runtime.InteropServices;

namespace Cosmos.Memory.Test;

public class UnitTest1
{
    [Fact]
    public unsafe void Test1()
    {
        var memorySize = (int)PageAllocator.PageSize * 15;
        var memory = NativeMemory.AlignedAlloc((nuint)memorySize, PageAllocator.PageSize);
        var memorySpan = new Span<byte>(memory, memorySize);
        memorySpan.Fill(0x00);

        PageAllocator.Init((byte*)memory, (uint)memorySize);

        var pageTest = PageAllocator.AllocPages(PageType.Unmanaged, 2);
        var pageTestSpan = new Span<byte>(pageTest, (int)PageAllocator.PageSize * 2);
        pageTestSpan.Fill(0x55); // 85

        NativeMemory.AlignedFree(memory);
    }
}
