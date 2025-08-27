using System;

namespace Cosmos.Patcher.Logging;

public sealed class ConsoleBuildLogger : IBuildLogger
{
    public void Info(string message) => Console.WriteLine(message);
    public void Warn(string message) => Console.WriteLine($"[WARN] {message}");
    public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
    public void Error(Exception exception) => Console.WriteLine($"[ERROR] {exception}");
    public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
}

