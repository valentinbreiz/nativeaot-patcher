// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Logging;

public static class LoggerFactory
{
    public static ILogger<T> CreateLogger<T>() => new SerialLogger<T>();

    public static ILogger CreateLogger(string category) => new CategoryLogger(category);
}
