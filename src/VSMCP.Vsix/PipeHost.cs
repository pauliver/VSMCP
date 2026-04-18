using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Accepts named-pipe connections from VSMCP.Server and dispatches RPC calls to <see cref="RpcTarget"/>.
/// One pipe per VS instance: name = "VSMCP.&lt;pid&gt;". ACL = current user only.
/// </summary>
internal sealed class PipeHost : IDisposable
{
    private readonly VSMCPPackage _package;
    private readonly JoinableTaskFactory _jtf;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _pipeName;

    public PipeHost(VSMCPPackage package, JoinableTaskFactory jtf)
    {
        _package = package;
        _jtf = jtf;
        _pipeName = PipeNaming.ForCurrentProcess();
    }

    public void Start()
    {
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServerStream(_pipeName);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var target = new RpcTarget(_package, _jtf);
                var rpc = JsonRpc.Attach(server, target);
                // Each connection owns its own stream; don't await Completion here — we want to
                // accept the next connection immediately. The rpc/stream dispose together.
                _ = rpc.Completion.ContinueWith(_ => server.Dispose(), TaskScheduler.Default);
                server = null; // ownership transferred
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                // Swallow and keep listening — a bad client shouldn't take down the host.
                server?.Dispose();
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { return; }
            }
        }
    }

    private static NamedPipeServerStream CreateServerStream(string name)
    {
        var security = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User
                  ?? throw new InvalidOperationException("Cannot determine current user SID.");
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

        return new NamedPipeServerStream(
            pipeName: name,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024,
            pipeSecurity: security);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}
