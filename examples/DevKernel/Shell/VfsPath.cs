using System;
using System.Collections.Generic;

namespace DevKernel.Shell;

/// <summary>
/// Absolute-path helpers for the VFS paths the shell manipulates.
/// </summary>
internal static class VfsPath
{
    /// <summary>The VFS root, and the shell's initial working directory.</summary>
    public const string Root = "/";

    /// <summary>Separator between VFS path components.</summary>
    public const char Separator = '/';

    /// <summary>Path component meaning "the current directory".</summary>
    private const string SelfComponent = ".";

    /// <summary>Path component meaning "the parent directory".</summary>
    private const string ParentComponent = "..";

    /// <summary>Makes <paramref name="path"/> absolute, anchoring a relative one at <paramref name="cwd"/>.</summary>
    public static string Resolve(string cwd, string path)
    {
        if (path.Length > 0 && path[0] == Separator)
        {
            return path;
        }

        if (cwd == Root)
        {
            return Root + path;
        }

        return cwd + Separator + path;
    }

    /// <summary>Collapses <c>.</c> and <c>..</c> components; returns <see cref="Root"/> when nothing remains.</summary>
    public static string Normalize(string path)
    {
        string[] parts = path.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        List<string> stack = new();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part == SelfComponent)
            {
                continue;
            }

            if (part == ParentComponent)
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        if (stack.Count == 0)
        {
            return Root;
        }

        return Root + string.Join(Separator, stack);
    }

    /// <summary>Splits an absolute path into its parent directory and its last component.</summary>
    public static (string parent, string leaf) Split(string fullPath)
    {
        int slash = fullPath.LastIndexOf(Separator);
        if (slash <= 0)
        {
            return (Root, fullPath.TrimStart(Separator));
        }

        return (fullPath.Substring(0, slash), fullPath.Substring(slash + 1));
    }
}
