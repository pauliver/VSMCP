using System;
using System.IO;
using System.Threading;
using VSMCP.Core;

namespace VSMCP.Vsix;

/// <summary>
/// Minimal Debug-only logging sink for VSIX-internal diagnostics. Writes to
/// <c>%LOCALAPPDATA%\VSMCP\logs\vsix.log</c> in Debug builds; no-op in Release.
///
/// Designed for the ~200 silent <c>catch { }</c> blocks across the VSIX where the
/// failure is non-fatal (best-effort enrichment) but useful when investigating
/// "why didn't X surface?". Replace bodies with:
///
/// <code>
/// catch (Exception ex) { VsmcpLog.Debug("category", "what failed", ex); }
/// </code>
///
/// The log file is truncated on every VS launch (via the InitOnce static ctor) so
/// it doesn't grow unbounded. Threadsafe via a lock.
/// </summary>
internal static class VsmcpLog
{
    private static RollingLogSink? s_sink;
    private static int s_initOnce;

    private static RollingLogSink EnsureInit()
    {
        if (Interlocked.Exchange(ref s_initOnce, 1) == 0)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSMCP", "logs");
            var sink = new RollingLogSink(Path.Combine(dir, "vsix.log"), truncate: true);
            sink.Append($"--- VSMCP.Vsix log {DateTime.UtcNow:O} ---");
            Volatile.Write(ref s_sink, sink);
        }
        return Volatile.Read(ref s_sink) ?? new RollingLogSink(null);
    }

    /// <summary>Path to the log file, or null if init failed. For diagnostics tools.</summary>
    public static string? LogPath => EnsureInit().FilePath;

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Debug(string category, string message, Exception? ex = null)
    {
        var line = ex is null
            ? $"{DateTime.UtcNow:O} [{category}] {message}"
            : $"{DateTime.UtcNow:O} [{category}] {message} | {ex.GetType().Name}: {ex.Message}";
        EnsureInit().Append(line);
    }
}
