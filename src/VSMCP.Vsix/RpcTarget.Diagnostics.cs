using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;
using Process = System.Diagnostics.Process;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public Task<ProcessListResult> ProcessesListAsync(ProcessListFilter? filter, CancellationToken cancellationToken = default)
    {
        filter ??= new ProcessListFilter();
        var result = new ProcessListResult();
        var currentSession = Process.GetCurrentProcess().SessionId;
        var needle = filter.NameContains;

        foreach (var p in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (filter.CurrentSessionOnly && p.SessionId != currentSession) { p.Dispose(); continue; }
                if (!string.IsNullOrEmpty(needle) && p.ProcessName.IndexOf(needle!, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    p.Dispose();
                    continue;
                }

                var row = new ProcessInfoRow
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    SessionId = p.SessionId,
                };

                try { row.WorkingSetBytes = p.WorkingSet64; } catch { }
                try { row.PrivateMemoryBytes = p.PrivateMemorySize64; } catch { }
                try { row.ThreadCount = p.Threads.Count; } catch { }
                try { row.MainModulePath = p.MainModule?.FileName; } catch { /* protected process */ }
                try { row.StartedUtc = p.StartTime.ToUniversalTime().ToString("o"); } catch { }

                result.Processes.Add(row);
            }
            catch
            {
                result.Inaccessible++;
            }
            finally
            {
                p.Dispose();
            }
        }

        result.Processes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(result);
    }

    public async Task<CountersSnapshot> CountersGetAsync(int pid, int sampleMs, CancellationToken cancellationToken = default)
    {
        if (pid <= 0) throw new VsmcpException(ErrorCodes.NotFound, "Pid must be > 0.");
        if (sampleMs < 50) sampleMs = 50;
        if (sampleMs > 10_000) sampleMs = 10_000;

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { throw new VsmcpException(ErrorCodes.NotFound, $"No process with pid {pid}."); }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Cannot open pid {pid}: {ex.Message}", ex); }

        try
        {
            var logicalCpus = Environment.ProcessorCount;
            TimeSpan cpu1;
            try { cpu1 = process.TotalProcessorTime; }
            catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Cannot read CPU time for pid {pid} (rights?): {ex.Message}", ex); }
            var t1 = DateTime.UtcNow;

            await Task.Delay(sampleMs, cancellationToken).ConfigureAwait(false);

            process.Refresh();
            TimeSpan cpu2;
            try { cpu2 = process.TotalProcessorTime; }
            catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Cannot read CPU time (second sample) for pid {pid}: {ex.Message}", ex); }
            var t2 = DateTime.UtcNow;

            var wallMs = (t2 - t1).TotalMilliseconds;
            var cpuMsDelta = (cpu2 - cpu1).TotalMilliseconds;
            var perCore = wallMs > 0 ? (cpuMsDelta / wallMs) * 100.0 : 0.0;
            var normalized = logicalCpus > 0 ? perCore / logicalCpus : perCore;

            var snap = new CountersSnapshot
            {
                Pid = pid,
                Name = process.ProcessName,
                SampleMs = sampleMs,
                CpuPercent = perCore,
                CpuPercentNormalized = normalized,
                LogicalProcessorCount = logicalCpus,
                TotalCpuTimeMs = (long)cpu2.TotalMilliseconds,
            };

            try { snap.WorkingSetBytes = process.WorkingSet64; } catch { }
            try { snap.PrivateMemoryBytes = process.PrivateMemorySize64; } catch { }
            try { snap.VirtualMemoryBytes = process.VirtualMemorySize64; } catch { }
            try { snap.PagedMemoryBytes = process.PagedMemorySize64; } catch { }
            try { snap.ThreadCount = process.Threads.Count; } catch { }
            try { snap.HandleCount = process.HandleCount; } catch { }
            try { snap.UptimeMs = (long)(DateTime.Now - process.StartTime).TotalMilliseconds; } catch { }

            return snap;
        }
        finally
        {
            process.Dispose();
        }
    }
}
