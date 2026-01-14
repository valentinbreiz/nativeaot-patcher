using System;
using System.Runtime.CompilerServices;

namespace Cosmos.Patcher.Logging;

public sealed class ConsoleBuildLogger : IBuildLogger
{
    public void Info(string message, [CallerMemberName] string caller = "") => Console.WriteLine($"[{caller}] {message}");
    public void Warn(string message, [CallerMemberName] string caller = "") => Console.WriteLine($"[WARN] [{caller}] {message}");
    public void Error(string message, [CallerMemberName] string caller = "") => Console.WriteLine($"[ERROR] [{caller}] {message}");
    public void Error(Exception exception, [CallerMemberName] string caller = "") => Console.WriteLine($"[ERROR] [{caller}] {exception}");
    public void Debug(string message, [CallerMemberName] string caller = "") => Console.WriteLine($"[DEBUG] [{caller}] {message}");
}

