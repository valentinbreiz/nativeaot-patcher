// This code is licensed under MIT license (see LICENSE for details)

using System;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System;

/// <summary>
/// Plug for System.Array to bypass EqualityComparer type loader requirements.
/// The NativeAOT Array.IndexOfImpl/LastIndexOfImpl use EqualityComparerHelpers
/// which require TypeLoaderCallbacks to construct generic comparers at runtime.
/// This plug provides simple implementations using Object.Equals.
/// </summary>
[Plug("System.Array")]
public static class ArrayPlug
{
    /// <summary>
    /// Simple IndexOf implementation that uses Object.Equals for comparison.
    /// This avoids the need for EqualityComparer&lt;T&gt; construction via TypeLoaderCallbacks.
    /// Uses static object.Equals to avoid constrained calls that don't work with canonical types.
    /// </summary>
    [PlugMember(TargetName = "IndexOfImpl")]
    private static int IndexOfImpl<T>(T[] array, T value, int startIndex, int count)
    {
        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (object.Equals(array[i], value))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Simple LastIndexOf implementation that uses Object.Equals for comparison.
    /// This avoids the need for EqualityComparer&lt;T&gt; construction via TypeLoaderCallbacks.
    /// Uses static object.Equals to avoid constrained calls that don't work with canonical types.
    /// </summary>
    [PlugMember(TargetName = "LastIndexOfImpl")]
    private static int LastIndexOfImpl<T>(T[] array, T value, int startIndex, int count)
    {
        int endIndex = startIndex - count + 1;
        for (int i = startIndex; i >= endIndex; i--)
        {
            if (object.Equals(array[i], value))
                return i;
        }
        return -1;
    }
}
