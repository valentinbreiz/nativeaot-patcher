namespace Cosmos.Build.Analyzer.Patcher.Extensions;

public static class EnumerableExtensions
{

    public static bool Any<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, out T? value)
    {
        using IEnumerator<T> enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (!predicate(enumerator.Current)) continue;
            value = enumerator.Current;
            return true;
        }

        value = default;
        return false;
    }

    
}
