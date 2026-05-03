// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Logging;

public interface ILogger
{
    void Log(LogLevel level, string message);
}

public interface ILogger<out T> : ILogger { }
