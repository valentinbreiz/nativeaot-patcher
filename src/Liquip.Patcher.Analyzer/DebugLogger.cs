using System.Runtime.CompilerServices;
namespace Liquip.Patcher.Analyzer;
public static class DebugLogger
{
    public static bool IsDebug { get; set; } = true;

    public static void Log(string message, [CallerMemberName] string callerName = "")
    {
        if (IsDebug)
        {
            Console.WriteLine($"[{callerName}] {message}");
        }
    }
}
