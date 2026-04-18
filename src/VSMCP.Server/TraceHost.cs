using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Owns active ETW trace sessions (TraceEventSession). Each session streams a .etl to disk until stopped.
/// Requires admin privileges — ETW user-mode sessions need SeSystemProfilePrivilege or Administrator.
/// </summary>
public sealed class TraceHost : IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveTrace> _sessions = new(StringComparer.Ordinal);

    public TraceStartResult Start(TraceStartOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        EnsureAdmin();

        var outPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Path.GetTempPath(), $"vsmcp-trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}.etl")
            : Path.GetFullPath(options.OutputPath!);
        var parent = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Parent directory does not exist: {parent}");

        var sessionName = "VSMCP-Trace-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        TraceEventSession session;
        try
        {
            session = new TraceEventSession(sessionName, outPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create ETW session: {ex.Message}", ex);
        }

        if (options.BufferSizeMB is int mb && mb > 0)
            session.BufferSizeMB = mb;

        var enabled = new List<string>();
        bool kernelEnabled = false;

        try
        {
            if (options.KernelKeywords is { Count: > 0 })
            {
                var kw = ParseKernelKeywords(options.KernelKeywords);
                try
                {
                    session.EnableKernelProvider(kw);
                    kernelEnabled = true;
                    enabled.Add("KernelTrace:" + kw);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"EnableKernelProvider failed (requires admin + Windows 8+ for user-kernel merge): {ex.Message}", ex);
                }
            }

            foreach (var p in options.Providers)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                var level = p.Level <= 0 ? TraceEventLevel.Informational : (TraceEventLevel)Math.Min(p.Level, 5);
                var keywords = p.Keywords;
                if (Guid.TryParse(p.Name.Trim('{', '}'), out var guid))
                    session.EnableProvider(guid, level, unchecked((ulong)keywords));
                else
                    session.EnableProvider(p.Name, level, unchecked((ulong)keywords));
                enabled.Add(p.Name);
            }
        }
        catch
        {
            try { session.Dispose(); } catch { }
            throw;
        }

        var id = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        _sessions[id] = new ActiveTrace(id, sessionName, outPath, session, started);

        return new TraceStartResult
        {
            SessionId = id,
            OutputPath = outPath,
            StartedUtc = started.ToString("o"),
            KernelEnabled = kernelEnabled,
            ProvidersEnabled = enabled,
        };
    }

    public TraceStopResult Stop(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var s))
            throw new InvalidOperationException($"No active trace session '{sessionId}'.");

        try { s.Session.Flush(); } catch { }
        try { s.Session.Dispose(); } catch { }

        long size = 0;
        try { size = new FileInfo(s.OutputPath).Length; } catch { }
        return new TraceStopResult
        {
            SessionId = sessionId,
            OutputPath = s.OutputPath,
            BytesWritten = size,
            DurationSeconds = (DateTimeOffset.UtcNow - s.StartedUtc).TotalSeconds,
        };
    }

    public TraceReport Report(string etlPath, int top)
    {
        if (string.IsNullOrWhiteSpace(etlPath)) throw new ArgumentException("Trace path is required.", nameof(etlPath));
        etlPath = Path.GetFullPath(etlPath);
        if (!File.Exists(etlPath)) throw new FileNotFoundException("Trace file not found.", etlPath);
        if (top <= 0) top = 20;
        if (top > 1000) top = 1000;

        var report = new TraceReport { Path = etlPath };
        var byKey = new Dictionary<(string provider, string evt), long>();
        var byProvider = new Dictionary<string, long>(StringComparer.Ordinal);
        long total = 0;
        double durationSec = 0;

        try
        {
            using var source = new ETWTraceEventSource(etlPath);
            DateTime first = DateTime.MinValue, last = DateTime.MinValue;
            source.AllEvents += ev =>
            {
                var provider = string.IsNullOrEmpty(ev.ProviderName) ? ev.ProviderGuid.ToString() : ev.ProviderName;
                var name = ev.EventName ?? "Unknown";
                var key = (provider, name);
                byKey[key] = byKey.TryGetValue(key, out var c) ? c + 1 : 1;
                byProvider[provider] = byProvider.TryGetValue(provider, out var pc) ? pc + 1 : 1;
                total++;
                if (first == DateTime.MinValue) first = ev.TimeStamp;
                last = ev.TimeStamp;
            };
            source.Process();
            if (first != DateTime.MinValue && last != DateTime.MinValue)
                durationSec = (last - first).TotalSeconds;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read .etl: {ex.Message}", ex);
        }

        report.TotalEvents = total;
        report.DurationSeconds = durationSec;
        report.EventsByProvider = byProvider;
        if (total == 0) { report.Empty = true; return report; }

        var ordered = new List<KeyValuePair<(string provider, string evt), long>>(byKey);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < Math.Min(top, ordered.Count); i++)
        {
            var e = ordered[i];
            report.TopEvents.Add(new TraceEventCount
            {
                Provider = e.Key.provider,
                EventName = e.Key.evt,
                Count = e.Value,
            });
        }
        return report;
    }

    public IReadOnlyList<string> ActiveSessionIds()
    {
        var keys = new List<string>(_sessions.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public void Dispose()
    {
        foreach (var kv in _sessions)
        {
            try { kv.Value.Session.Dispose(); } catch { }
        }
        _sessions.Clear();
    }

    private static KernelTraceEventParser.Keywords ParseKernelKeywords(List<string> names)
    {
        KernelTraceEventParser.Keywords combined = KernelTraceEventParser.Keywords.None;
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (!Enum.TryParse<KernelTraceEventParser.Keywords>(n, ignoreCase: true, out var k))
                throw new ArgumentException($"Unknown kernel keyword '{n}'. See KernelTraceEventParser.Keywords for valid names.");
            combined |= k;
        }
        return combined;
    }

    private static void EnsureAdmin()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ETW tracing is Windows-only.");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException(
                "trace.start requires Administrator privileges. Re-launch VSMCP.Server elevated or use profiler.start (EventPipe) which does not require admin.");
    }

    private sealed record ActiveTrace(
        string SessionId,
        string WindowsSessionName,
        string OutputPath,
        TraceEventSession Session,
        DateTimeOffset StartedUtc);
}
