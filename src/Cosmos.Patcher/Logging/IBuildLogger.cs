using System;
using System.Runtime.CompilerServices;

namespace Cosmos.Patcher.Logging;

public interface IBuildLogger
{
    void Info(string message, [CallerMemberName] string caller = "");
    void Warn(string message, [CallerMemberName] string caller = "");
    void Error(string message, [CallerMemberName] string caller = "");
    void Error(Exception exception, [CallerMemberName] string caller = "");
    void Debug(string message, [CallerMemberName] string caller = "");
}

