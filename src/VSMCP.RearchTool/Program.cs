using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VSMCP.Shared;

namespace VSMCP.RearchTool;

/// <summary>
/// Direct-pipe client for the VSMCP VSIX. Bypasses VSMCP.Server entirely so the active
/// Claude Code MCP connection is irrelevant — this talks straight to the named pipe the
/// VSIX exposes inside devenv.exe.
///
/// Usage:
///   vsmcp-rearch ping
///   vsmcp-rearch status
///   vsmcp-rearch move-many [--no-dry-run] [--no-update-project] &lt;mapping.json&gt;
///
/// mapping.json shape: [{"From":"...","To":"..."}, ...]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { PrintUsage(); return 0; }
            var cmd = args[0].ToLowerInvariant();

            await using var conn = await ConnectAsync().ConfigureAwait(false);

            switch (cmd)
            {
                case "ping":
                {
                    var p = await conn.Proxy.PingAsync().ConfigureAwait(false);
                    Console.WriteLine($"{p.Message} ts={p.ServerTimestampMs}");
                    return 0;
                }
                case "status":
                {
                    var s = await conn.Proxy.GetStatusAsync().ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "move-many":
                {
                    bool dryRun = !args.Contains("--no-dry-run");
                    bool updateProject = !args.Contains("--no-update-project");
                    var jsonPath = args.LastOrDefault(a => !a.StartsWith("--") && a != "move-many")
                        ?? throw new InvalidOperationException("provide mapping.json path");
                    var json = File.ReadAllText(jsonPath);
                    var pairs = JsonSerializer.Deserialize<List<FileMovePair>>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new InvalidOperationException("empty mapping");

                    Console.Error.WriteLine($"[move-many] pairs={pairs.Count} dryRun={dryRun} updateProject={updateProject}");
                    var result = await conn.Proxy.FileMoveManyAsync(pairs, updateProject, dryRun).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[move-many] total={result.Total} moved={result.MovedCount} csprojEdits={result.CsprojEdits} skipped={result.SkippedCount}");
                    return 0;
                }
                case "move-types":
                {
                    var jsonPath = args.LastOrDefault(a => !a.StartsWith("--") && a != "move-types")
                        ?? throw new InvalidOperationException("provide moves.json path");
                    var json = File.ReadAllText(jsonPath);
                    var moves = JsonSerializer.Deserialize<List<MoveTypeRequest>>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new InvalidOperationException("empty moves list");

                    Console.Error.WriteLine($"[move-types] count={moves.Count}");
                    int ok = 0, fail = 0;
                    foreach (var m in moves)
                    {
                        try
                        {
                            var r = await conn.Proxy.EditMoveTypeAsync(m.File, m.TypeName, m.NewNamespace, m.NewFile, m.AppendIfExists)
                                              .ConfigureAwait(false);
                            if (r.Success) { ok++; Console.Error.WriteLine($"  ok    {m.TypeName,-32} -> {m.NewFile}"); }
                            else if (r.Conflict) { fail++; Console.Error.WriteLine($"  CONFL {m.TypeName,-32} -> {m.NewFile}  (set appendIfExists)"); }
                            else { fail++; Console.Error.WriteLine($"  FAIL  {m.TypeName,-32} -> {m.NewFile}  (type not found in source)"); }
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            Console.Error.WriteLine($"  THROW {m.TypeName,-32} -> {m.NewFile}  ({ex.GetType().Name}: {ex.Message})");
                        }
                    }
                    Console.Error.WriteLine($"[move-types] ok={ok} fail={fail}");
                    return fail == 0 ? 0 : 1;
                }
                default:
                    Console.Error.WriteLine($"unknown command: {cmd}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vsmcp-rearch: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("vsmcp-rearch ping");
        Console.WriteLine("vsmcp-rearch status");
        Console.WriteLine("vsmcp-rearch move-many [--no-dry-run] [--no-update-project] <mapping.json>");
        Console.WriteLine("vsmcp-rearch move-types <moves.json>   # [{File, TypeName, NewFile, NewNamespace?, AppendIfExists?}]");
    }

    public sealed class MoveTypeRequest
    {
        public string File { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string? NewFile { get; set; }
        public string? NewNamespace { get; set; }
        public bool AppendIfExists { get; set; }
    }

    private static async Task<Connection> ConnectAsync()
    {
        // Find a VSMCP pipe.
        string? pipeName = null;
        try
        {
            var pipes = Directory.GetFiles(@"\\.\pipe\");
            pipeName = pipes
                .Select(Path.GetFileName)
                .Where(n => n is not null && n!.StartsWith("VSMCP.", StringComparison.Ordinal))
                .FirstOrDefault();
        }
        catch { }
        if (pipeName is null)
            throw new InvalidOperationException("no VSMCP.<pid> pipe found — is VS running with the VSMCP extension loaded?");

        var stream = new NamedPipeClientStream(".", pipeName!, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000).ConfigureAwait(false);

        var handler = new HeaderDelimitedMessageHandler(stream);
        var rpc = new JsonRpc(handler)
        {
            ExceptionStrategy = ExceptionProcessing.ISerializable,
        };
        var proxy = rpc.Attach<IVsmcpRpc>();
        rpc.StartListening();

        await proxy.HandshakeAsync(ProtocolVersion.Major, ProtocolVersion.Minor).ConfigureAwait(false);
        return new Connection(stream, rpc, proxy);
    }

    private sealed class Connection : IAsyncDisposable
    {
        public NamedPipeClientStream Stream { get; }
        public JsonRpc Rpc { get; }
        public IVsmcpRpc Proxy { get; }

        public Connection(NamedPipeClientStream stream, JsonRpc rpc, IVsmcpRpc proxy)
        {
            Stream = stream;
            Rpc = rpc;
            Proxy = proxy;
        }

        public ValueTask DisposeAsync()
        {
            try { Rpc.Dispose(); } catch { }
            try { Stream.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
