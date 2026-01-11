// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.FileSystem;

namespace DevKernel;

public static class Fs
{

    static void Log(string message)
    {
        Serial.Write(message);
        Console.Write(message);
    }

    public static void Ls(string[] parts)
    {

        var path = parts[1];
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        var items = Vfs.ListDirectory(path);
        var i = 0;
        foreach (var item in items)
        {
            i++;
            var line = i + " " + item.InodeNumber + " " + path + item.Name + (item.IsDirectory ? "/" : "") + " " +
                       item.Size + "\n";
            Log(line);
        }

    }

    public static void Cat(string[] parts)
    {

        var fd = Vfs.Open(parts[1], FileAccessMode.Read, false);
        if (fd == null)
        {
            Log("Cound not find file");
            return;
        }

        if (!fd.Inode?.IsFile?? false)
        {
            Log("This is not a File");
        }

        using var stream = Vfs.GetStream(fd);

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        Log(reader.ReadToEnd());
        Vfs.Close(fd);


    }

}
