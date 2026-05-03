// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Logging;

public sealed class SerialLogger<T> : ILogger<T>
{
    private static readonly string s_category = typeof(T).Name;

    public void Log(LogLevel level, string message)
        => LogBackend.Write(s_category, level, message);
}

public sealed class CategoryLogger : ILogger
{
    private readonly string _category;

    public CategoryLogger(string category)
    {
        _category = category;
    }

    public void Log(LogLevel level, string message)
        => LogBackend.Write(_category, level, message);
}
