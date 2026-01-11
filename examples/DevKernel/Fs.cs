// This code is licensed under MIT license (see LICENSE for details)

using System;
using Cosmos.Kernel.System;

namespace DevKernel;

public static class FsTests
{

    public static void Ls(string[] parts)
    {
        var items = Vfs.ListDirectory(parts[1]);
        foreach (var item in items)
        {
            Console.WriteLine("{0} {1} {2}", item.InodeNumber, item.Name, item.Size);
        }

    }

}
