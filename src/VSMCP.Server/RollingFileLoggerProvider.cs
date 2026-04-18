using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VSMCP.Server;

/// <summary>
/// Minimal date-rolling file logger. Writes one file per day under the configured directory
/// with name <c>server-YYYYMMDD.log</c>. Opt-in; kept dependency-free so we don't pull Serilog
/// into a tool package.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new();

    public RollingFileLoggerProvider(string directory, LogLevel minLevel)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(name, this));

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        if (level < _minLevel) return;
        var path = Path.Combine(_directory, $"server-{DateTime.UtcNow:yyyyMMdd}.log");
        var sb = new StringBuilder(256);
        sb.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        sb.Append(' ').Append(LevelTag(level));
        sb.Append(' ').Append(category);
        sb.Append(' ').Append(message);
        if (ex is not null) sb.Append(' ').Append(ex);
        sb.Append('\n');

        lock (_writeLock)
        {
            try { File.AppendAllText(path, sb.ToString()); }
            catch { /* file locked / disk full — don't crash the bridge */ }
        }
    }

    public void Dispose() => _loggers.Clear();

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "NUL",
    };

    private sealed class RollingFileLogger : ILogger
    {
        private readonly string _category;
        private readonly RollingFileLoggerProvider _owner;

        public RollingFileLogger(string category, RollingFileLoggerProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _owner._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            if (string.IsNullOrEmpty(msg) && exception is null) return;
            _owner.Write(_category, logLevel, msg, exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
