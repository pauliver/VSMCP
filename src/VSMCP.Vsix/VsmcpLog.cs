using System;
using System.IO;
using System.Threading;
using VSMCP.Core;

namespace VSMCP.Vsix;

/// <summary>
/// Minimal logging sink for VSIX-internal diagnostics. Writes to
/// <c>%LOCALAPPDATA%\VSMCP\logs\vsix.log</c> in ALL build configurations — a failing
/// tool call at a stranger's desk runs a Release build, and a log that only exists in
/// Debug can't explain it. Bounded by <see cref="RollingLogSink"/> and truncated on
/// each VS launch; set <see cref="Enabled"/> false to silence at runtime.
///
/// Designed for the ~200 silent <c>catch { }</c> blocks across the VSIX where the
/// failure is non-fatal (best-effort enrichment) but useful when investigating
/// "why didn't X surface?". Replace bodies with:
///
/// <code>
/// catch (Exception ex) { VsmcpLog.Debug("category", "what failed", ex); }
/// </code>
///
/// Lines carry the ambient correlation ActivityId (propagated from the server per RPC
/// via StreamJsonRpc activity tracing) so a failure is traceable end-to-end by one id.
/// </summary>
internal static class VsmcpLog
{
    /// <summary>Runtime gate. Defaults on; flip off to silence without rebuilding.</summary>
    public static bool Enabled { get; set; } = true;

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

    public static void Debug(string category, string message, Exception? ex = null)
    {
        if (!Enabled) return;

        var aid = System.Diagnostics.Trace.CorrelationManager.ActivityId;
        var idPart = aid == Guid.Empty ? "" : $" [{aid.ToString("N").Substring(0, 8)}]";
        var line = ex is null
            ? $"{DateTime.UtcNow:O}{idPart} [{category}] {message}"
            : $"{DateTime.UtcNow:O}{idPart} [{category}] {message} | {ex.GetType().Name}: {ex.Message}";
        EnsureInit().Append(line);
    }
}
