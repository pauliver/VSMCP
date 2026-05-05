using System;
using System.IO;
using System.Threading;

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
    private static readonly object s_lock = new();
    private static string? s_logPath;
    private static int s_initOnce;

    private static void EnsureInit()
    {
        if (Interlocked.Exchange(ref s_initOnce, 1) != 0) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSMCP", "logs");
            Directory.CreateDirectory(dir);
            s_logPath = Path.Combine(dir, "vsix.log");
            File.WriteAllText(s_logPath, $"--- VSMCP.Vsix log {DateTime.UtcNow:O} ---{Environment.NewLine}");
        }
        catch
        {
            s_logPath = null;
        }
    }

    /// <summary>Path to the log file, or null if init failed. For diagnostics tools.</summary>
    public static string? LogPath
    {
        get { EnsureInit(); return s_logPath; }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Debug(string category, string message, Exception? ex = null)
    {
        try
        {
            EnsureInit();
            if (s_logPath is null) return;
            var line = ex is null
                ? $"{DateTime.UtcNow:O} [{category}] {message}"
                : $"{DateTime.UtcNow:O} [{category}] {message} | {ex.GetType().Name}: {ex.Message}";
            lock (s_lock)
            {
                File.AppendAllText(s_logPath, line + Environment.NewLine);
            }
        }
        catch { /* logging must never throw */ }
    }
}
