namespace Cosmos.Patcher.Logging;

public interface IBuildLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Error(System.Exception exception);
    void Debug(string message);
}

