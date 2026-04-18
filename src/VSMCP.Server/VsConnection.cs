using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Owns the named-pipe connection to a single Visual Studio VSMCP host.
/// Thread-safe; single instance per process; reconnects on demand.
/// </summary>
public sealed class VsConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _stream;
    private JsonRpc? _rpc;
    private IVsmcpRpc? _proxy;
    private int? _connectedPid;

    public int? ConnectedProcessId => _connectedPid;

    public bool IsConnected => _rpc is { IsDisposed: false } && _stream is { IsConnected: true };

    public async Task<IVsmcpRpc> GetOrConnectAsync(CancellationToken ct)
    {
        if (IsConnected && _proxy is not null) return _proxy;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsConnected && _proxy is not null) return _proxy;

            var instances = ListInstances();
            if (instances.Count == 0)
                throw new InvalidOperationException($"{ErrorCodes.NotConnected}: no running Visual Studio 2022 instance with the VSMCP extension was found. Open VS and ensure the VSMCP extension is installed.");

            if (instances.Count > 1)
                throw new InvalidOperationException($"{ErrorCodes.NotConnected}: multiple VS instances found ({instances.Count}). Call vs.list_instances and vs.select first.");

            await ConnectToAsync(instances[0].ProcessId, ct).ConfigureAwait(false);
            return _proxy!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ConnectToAsync(int pid, CancellationToken ct)
    {
        await DisposeCurrentAsync().ConfigureAwait(false);

        var pipeName = PipeNaming.ForProcess(pid);
        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000, ct).ConfigureAwait(false);

        var rpc = JsonRpc.Attach(stream);
        var proxy = rpc.Attach<IVsmcpRpc>();

        var hs = await proxy.HandshakeAsync(ProtocolVersion.Major, ProtocolVersion.Minor, ct).ConfigureAwait(false);
        if (hs.ProtocolMajor != ProtocolVersion.Major)
        {
            rpc.Dispose();
            stream.Dispose();
            throw new InvalidOperationException(
                $"{ErrorCodes.UpgradeRequired}: bridge protocol v{ProtocolVersion.DisplayString} is incompatible with extension v{hs.ProtocolMajor}.{hs.ProtocolMinor}. Update both to matching versions.");
        }

        _stream = stream;
        _rpc = rpc;
        _proxy = proxy;
        _connectedPid = pid;
    }

    /// <summary>Enumerates VS processes that have a VSMCP pipe listening.</summary>
    public static IReadOnlyList<VsInstance> ListInstances()
    {
        // Windows exposes named pipes at \\.\pipe\. Enumerating this "directory" returns all pipe names.
        var found = new List<VsInstance>();
        string[] names;
        try
        {
            names = Directory.GetFiles(@"\\.\pipe\", $"{PipeNaming.Prefix}*");
        }
        catch
        {
            return found;
        }

        foreach (var path in names)
        {
            var name = Path.GetFileName(path);
            if (!PipeNaming.IsVsmcpPipe(name)) continue;
            var pidPart = name.Substring(PipeNaming.Prefix.Length);
            if (!int.TryParse(pidPart, out var pid)) continue;

            string? title = null;
            string? solution = null;
            try
            {
                var proc = Process.GetProcessById(pid);
                title = proc.MainWindowTitle;
                // Heuristic: VS window title often ends with "— Microsoft Visual Studio" and starts with solution name.
                if (!string.IsNullOrEmpty(title))
                {
                    var dash = title.IndexOf(" - ", StringComparison.Ordinal);
                    if (dash > 0) solution = title.Substring(0, dash);
                }
            }
            catch { /* process gone */ }

            found.Add(new VsInstance { ProcessId = pid, PipeName = name, MainWindowTitle = title, SolutionPath = solution });
        }

        return found;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private Task DisposeCurrentAsync()
    {
        try { _rpc?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _rpc = null;
        _stream = null;
        _proxy = null;
        _connectedPid = null;
        return Task.CompletedTask;
    }
}
