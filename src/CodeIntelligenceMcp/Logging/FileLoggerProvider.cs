using System.Globalization;

namespace CodeIntelligenceMcp.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxLogSizeBytes = 10 * 1024 * 1024;

    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLoggerProvider(string path)
    {
        RollIfOversized(path);

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        lock (_lock)
            _writer.WriteLine($"--- Session started {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ---");
    }

    private static void RollIfOversized(string path)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogSizeBytes)
                File.Move(path, path + ".old", overwrite: true);
        }
        catch (IOException)
        {
            // Another live server instance holds the file — keep appending without rotation.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _writer, _lock);

    public void Dispose() => _writer.Dispose();
}

internal sealed class FileLogger(string category, StreamWriter writer, object lockObj) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string level = logLevel switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "DBG"
        };

        string shortCategory = category.Contains('.')
            ? category[(category.LastIndexOf('.') + 1)..]
            : category;

        string message = formatter(state, exception);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

        lock (lockObj)
        {
            writer.WriteLine($"{timestamp} [{level}] {shortCategory}: {message}");
            if (exception is not null)
                writer.WriteLine($"  {exception}");
        }
    }
}
