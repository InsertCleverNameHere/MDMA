using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mdma.Core;

public enum MdmaLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MdmaLogLevel Level,
    string Component,
    string Message,
    string? Details = null,
    string? Exception = null
);

public interface IMdmaLogger
{
    void Log(
        MdmaLogLevel level,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    );
}

public static class LoggerExtensions
{
    public static void LogDebug(
        this IMdmaLogger logger,
        string component,
        string message,
        string? details = null
    ) => logger.Log(MdmaLogLevel.Debug, component, message, details);

    public static void LogInfo(
        this IMdmaLogger logger,
        string component,
        string message,
        string? details = null
    ) => logger.Log(MdmaLogLevel.Info, component, message, details);

    public static void LogWarning(
        this IMdmaLogger logger,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    ) => logger.Log(MdmaLogLevel.Warning, component, message, details, exception);

    public static void LogError(
        this IMdmaLogger logger,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    ) => logger.Log(MdmaLogLevel.Error, component, message, details, exception);
}

/// <summary>
/// Default no-op logger so components can accept IMdmaLogger as an optional dependency
/// without needing null checks everywhere.
/// </summary>
public sealed class NullMdmaLogger : IMdmaLogger
{
    public static NullMdmaLogger Instance { get; } = new();

    public void Log(
        MdmaLogLevel level,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    )
    {
        // No-op
    }
}

/// <summary>
/// Thread-safe file logger that writes JSON lines (.jsonl format) to
/// <workingRoot>\logs\mdma_{yyyyMMdd}.log. Logging errors are swallowed best-effort
/// so log drive failures never crash core application logic.
/// </summary>
public sealed class FileLogger : IMdmaLogger
{
    private const string LogsSubfolder = "logs";
    private readonly WorkingRoot _workingRoot;
    private readonly IClock _clock;
    private readonly object _lock = new();

    public FileLogger(WorkingRoot workingRoot, IClock? clock = null)
    {
        _workingRoot = workingRoot;
        _clock = clock ?? new RealClock();
    }

    public void Log(
        MdmaLogLevel level,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    )
    {
        var now = _clock.UtcNow;
        var entry = new LogEntry(
            Timestamp: now,
            Level: level,
            Component: component,
            Message: message,
            Details: details,
            Exception: exception?.ToString()
        );

        string jsonLine;
        try
        {
            jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
        }
        catch
        {
            return; // Serialization failure should never throw
        }

        lock (_lock)
        {
            try
            {
                var logsDir = Path.Combine(_workingRoot.Path, LogsSubfolder);
                Directory.CreateDirectory(logsDir);

                var logFilePath = Path.Combine(logsDir, $"mdma_{now:yyyyMMdd}.log");
                File.AppendAllText(logFilePath, jsonLine);
            }
            catch
            {
                // Best-effort write: if the disk is full, unmounted, or locked, swallow the exception
                // so a logging failure never aborts the core download migration operation.
            }
        }
    }

    private sealed class RealClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
