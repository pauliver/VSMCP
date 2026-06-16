using System;
using System.IO;

namespace VSMCP.Core;

/// <summary>
/// Minimal thread-safe append-to-file log sink (#130), extracted so it can be unit-tested
/// without the VS SDK. Truncates on construction so the file doesn't grow unbounded across
/// sessions. Every operation is best-effort: a logging failure never throws into the caller.
/// </summary>
public sealed class RollingLogSink
{
    private readonly object _lock = new();
    private string? _path;

    public RollingLogSink(string? path, bool truncate = true)
    {
        _path = path;
        if (_path is null) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (truncate) File.WriteAllText(_path, "");
        }
        catch
        {
            _path = null; // disable rather than throw if the location is unwritable
        }
    }

    /// <summary>Resolved log path, or null when init failed / no path was given.</summary>
    public string? FilePath => _path;

    public void Append(string line)
    {
        if (_path is null) return;
        try
        {
            lock (_lock) File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
    }
}
