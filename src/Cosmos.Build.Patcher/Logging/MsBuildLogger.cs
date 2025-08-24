using System;
using Cosmos.Patcher.Logging;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.Patcher.Logging;

public sealed class MsBuildLogger : IBuildLogger
{
    private readonly TaskLoggingHelper _log;

    public MsBuildLogger(TaskLoggingHelper log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Info(string message) => _log.LogMessage(MessageImportance.Normal, message);
    public void Debug(string message) => _log.LogMessage(MessageImportance.Low, message);
    public void Warn(string message) => _log.LogWarning(message);
    public void Error(string message) => _log.LogError(message);
    public void Error(Exception exception) => _log.LogErrorFromException(exception, showStackTrace: true);
}

