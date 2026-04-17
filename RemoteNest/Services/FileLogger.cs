using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RemoteNest.Services;

/// <summary>
/// Simple rotating file logger. Writes to
/// <c>%LOCALAPPDATA%\RemoteNest\logs\remotenest-YYYYMMDD.log</c> and deletes logs older
/// than <see cref="RetentionDays"/>. Thread-safe; a single shared lock per provider keeps
/// writes serialized to avoid interleaved lines without sacrificing structured logging.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetentionDays = 7;

    public static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "RemoteNest", "logs");

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeLock = new();
    private readonly LogLevel _minLevel;
    private int _disposed;

    public FileLoggerProvider(LogLevel minLevel = LogLevel.Information)
    {
        _minLevel = minLevel;
        Directory.CreateDirectory(LogDirectory);
        TryPurgeOldLogs();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _minLevel, Write));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _loggers.Clear();
    }

    private void Write(string line)
    {
        var path = Path.Combine(LogDirectory, $"remotenest-{DateTime.Now:yyyyMMdd}.log");
        lock (_writeLock)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { /* never let logging crash the app */ }
        }
    }

    private void TryPurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "remotenest-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* skip locked/unreadable file */ }
            }
        }
        catch { /* never let logging crash the app */ }
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly LogLevel _minLevel;
    private readonly Action<string> _writeLine;

    public FileLogger(string category, LogLevel minLevel, Action<string> writeLine)
    {
        _category = category;
        _minLevel = minLevel;
        _writeLine = writeLine;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= _minLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag(logLevel)}] {_category} — {message}";
        if (exception is not null) line += $"{Environment.NewLine}{exception}";
        _writeLine(line);
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace       => "TRC",
        LogLevel.Debug       => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning     => "WRN",
        LogLevel.Error       => "ERR",
        LogLevel.Critical    => "CRT",
        _                    => "   "
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
