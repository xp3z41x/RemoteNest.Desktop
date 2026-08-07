namespace RemoteNest.Services;

/// <summary>
/// Minimal rotating file logger. Writes to
/// <c>%LOCALAPPDATA%\RemoteNest\logs\remotenest-YYYYMMDD.log</c> and deletes logs older
/// than <see cref="RetentionDays"/>. The app logs a handful of lines per session, so a
/// static class with a lock covers it. Logging must never throw.
/// </summary>
public static class Log
{
    private const int RetentionDays = 7;

    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "RemoteNest", "logs");

    private static readonly object WriteLock = new();

    /// <summary>Creates the log directory; purges old files off the startup path.</summary>
    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch { /* never let logging crash the app */ }

        Task.Run(PurgeOldLogs);
    }

    public static void Info(string message) => Write("INF", message, null);
    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);
    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);
    public static void Critical(string message, Exception? exception = null) => Write("CRT", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (exception is not null) line += $"{Environment.NewLine}{exception}";

        var path = Path.Combine(LogDirectory, $"remotenest-{DateTime.Now:yyyyMMdd}.log");
        lock (WriteLock)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { /* never let logging crash the app */ }
        }
    }

    private static void PurgeOldLogs()
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
