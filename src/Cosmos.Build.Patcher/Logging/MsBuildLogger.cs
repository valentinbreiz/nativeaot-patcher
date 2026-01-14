using System;
using Cosmos.Patcher.Logging;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.Patcher.Logging;

public sealed class MsBuildLogger(TaskLoggingHelper log) : IBuildLogger
{
    private readonly TaskLoggingHelper _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Info(string message, string caller = "") =>
        _log.LogMessage(MessageImportance.Normal, $"{caller}: {message}");

    public void Warn(string message, string caller = "") =>
        _log.LogWarning($"{caller}: {message}");

    public void Error(string message, string caller = "") =>
        _log.LogError($"{caller}: {message}");

    public void Error(Exception exception, string caller = "") =>
        _log.LogError($"{caller}: {exception}");

    public void Debug(string message, string caller = "") =>
        _log.LogMessage(MessageImportance.Low, $"{caller}: {message}");
}

