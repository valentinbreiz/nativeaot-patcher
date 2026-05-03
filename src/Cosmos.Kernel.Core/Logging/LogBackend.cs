// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Logging;

/// <summary>
/// Static log routing backend. All <c>Log.*</c> calls funnel through
/// <see cref="Write"/> so it is safe to call during
/// <c>LibraryInitializer</c> (no interface dispatch, no heap allocation
/// in the default path).
///
/// Usage:
/// - Framework code: tag the class with <c>[Logger]</c> + <c>partial</c> and
///   call <c>Log.Info / Log.Debug / Log.Error / ...</c> on the generated
///   nested proxy.
/// - Post-init user code wanting the .NET pattern:
///   <c>LoggerFactory.CreateLogger&lt;T&gt;()</c> returns an
///   <see cref="ILogger{T}"/> that routes through the same backend.
///
/// To redirect logs to a different sink (file, network, framebuffer, ring
/// buffer) install a <see cref="Sink"/> delegate via <see cref="SetSink"/>
/// once the runtime is ready (e.g. from <c>Kernel.BeforeRun</c>).
/// </summary>
public static class LogBackend
{
    public static LogLevel MinimumLevel = LogLevel.Information;

    public delegate void Sink(string category, LogLevel level, string message);

    private static Sink? s_sink;

    public static void Write(string category, LogLevel level, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        Sink? sink = s_sink;
        if (sink != null)
        {
            sink(category, level, message);
        }
        else
        {
            DirectWrite(category, level, message);
        }
    }

    public static void SetSink(Sink? sink)
    {
        s_sink = sink;
    }

    public static void ResetSink()
    {
        s_sink = null;
    }

    private static void DirectWrite(string category, LogLevel level, string message)
    {
        Serial.WriteString("[");
        Serial.WriteString(LevelTag(level));
        Serial.WriteString("] [");
        Serial.WriteString(category);
        Serial.WriteString("] ");
        Serial.WriteString(message);
        Serial.WriteString("\n");
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };
}
