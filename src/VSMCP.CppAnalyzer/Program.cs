using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VSMCP.Shared;

namespace VSMCP.CppAnalyzer;

/// <summary>
/// VSMCP.CppAnalyzer entry point. Launched lazily by VSMCP.Vsix when the first cpp_*
/// semantic call arrives. Listens on a per-VS pipe (`VSMCP.Cpp.&lt;vsPid&gt;`) and
/// dispatches to <see cref="CppAnalysisService"/>.
///
/// Usage:
///   vsmcp-cppanalyzer --vs-pid &lt;pid&gt;
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        int? vsPid = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--vs-pid" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                vsPid = p;
        }
        if (vsPid is null)
        {
            await Console.Error.WriteLineAsync("vsmcp-cppanalyzer: missing --vs-pid <pid> argument");
            return 1;
        }

        var pipeName = PipeNaming.ForCppAnalyzer(vsPid.Value);
        await Console.Error.WriteLineAsync($"vsmcp-cppanalyzer: pipe={pipeName} pid={Environment.ProcessId}");

        // Single client, but use MaxAllowedServerInstances so reconnects work cleanly.
        using var server = new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024);

        // 60-second wait for the VSIX to connect; if it doesn't, exit so we don't pin a pipe.
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await server.WaitForConnectionAsync(connectCts.Token); }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("vsmcp-cppanalyzer: client never connected; exiting");
            return 2;
        }
        await Console.Error.WriteLineAsync("vsmcp-cppanalyzer: client connected");

        var service = new CppAnalysisService();
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(server))
        {
            ExceptionStrategy = ExceptionProcessing.ISerializable,
        };
        rpc.AddLocalRpcTarget<IVsmcpCppRpc>(service, options: null);
        rpc.StartListening();

        // Idle exit if the VSIX disposes the connection.
        await rpc.Completion.ContinueWith(_ => { }, TaskScheduler.Default);
        await Console.Error.WriteLineAsync("vsmcp-cppanalyzer: connection closed; exiting");
        service.Dispose();
        return 0;
    }
}
