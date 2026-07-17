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
                    Console.Error.WriteLine($"[move-many] total={result.Total} moved={result.MovedCount} csprojEdits={result.CsprojEdits} skipped={result.SkippedCount} errors={result.ErrorCount}");
                    // Per-pair failures no longer throw (they're recorded on the outcomes), so the
                    // exit code is what scripted callers key on — partial failure must be non-zero.
                    if (result.ErrorCount > 0)
                    {
                        foreach (var o in result.Outcomes)
                            if (o.Error is not null)
                                Console.Error.WriteLine($"[move-many] FAILED {o.From} -> {o.To}: {o.Error}");
                        return 1;
                    }
                    return 0;
                }
                case "load-folder":
                {
                    var root = args.LastOrDefault(a => !a.StartsWith("--") && a != "load-folder");
                    var r = await conn.Proxy.ProjectLoadWorkspaceFolderAsync(root).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[load-folder] root={r.Root} projects={r.Projects.Count} totalDocs={r.TotalDocumentsAdded}");
                    return 0;
                }
                case "sidecar-status":
                {
                    var r = await conn.Proxy.ProjectSidecarStatusAsync().ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "find-symbol":
                {
                    var name = args.LastOrDefault(a => !a.StartsWith("--") && a != "find-symbol")
                        ?? throw new InvalidOperationException("provide symbol name");
                    var r = await conn.Proxy.CodeFindSymbolAsync(name, null, 50, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[find-symbol] name={name} matches={r.Matches.Count}");
                    return 0;
                }
                case "cpp-outline":
                {
                    var file = args.LastOrDefault(a => !a.StartsWith("--") && a != "cpp-outline")
                        ?? throw new InvalidOperationException("provide file path");
                    var r = await conn.Proxy.CppOutlineAsync(file, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-outline] file={r.File} decls={r.Total} truncated={r.Truncated}");
                    return 0;
                }
                case "cpp-classes":
                {
                    var name = args.LastOrDefault(a => !a.StartsWith("--") && a != "cpp-classes");
                    var r = await conn.Proxy.CppClassesAsync(name, null, 1000, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-classes] total={r.Total}");
                    return 0;
                }
                case "cpp-find-symbol":
                {
                    var nonOpts = args.Where(a => !a.StartsWith("--") && a != "cpp-find-symbol").ToList();
                    if (nonOpts.Count < 1) throw new InvalidOperationException("usage: cpp-find-symbol <name>");
                    var r = await conn.Proxy.CppFindSymbolAsync(nonOpts[0], null, 100, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-find-symbol] total={r.Total}");
                    return 0;
                }
                case "cpp-diagnostics":
                {
                    var file = args.LastOrDefault(a => !a.StartsWith("--") && a != "cpp-diagnostics")
                        ?? throw new InvalidOperationException("provide file path");
                    var r = await conn.Proxy.CppDiagnosticsAsync(file, null, null, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-diagnostics] file={r.File} count={r.Diagnostics.Count} hasErrors={r.HasErrors}");
                    return 0;
                }
                case "cpp-quick-info":
                {
                    var nonOpts = args.Where(a => !a.StartsWith("--") && a != "cpp-quick-info").ToList();
                    if (nonOpts.Count < 3) throw new InvalidOperationException("usage: cpp-quick-info <file> <line> <col>");
                    var r = await conn.Proxy.CppQuickInfoAsync(nonOpts[0], int.Parse(nonOpts[1]), int.Parse(nonOpts[2]), null, null, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "cpp-find-refs":
                {
                    var nonOpts = args.Where(a => !a.StartsWith("--") && a != "cpp-find-refs").ToList();
                    if (nonOpts.Count < 3) throw new InvalidOperationException("usage: cpp-find-refs <file> <line> <col>");
                    var r = await conn.Proxy.CppFindReferencesSemAsync(nonOpts[0], int.Parse(nonOpts[1]), int.Parse(nonOpts[2]), null, null, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-find-refs] spelling={r.Spelling} kind={r.Kind} total={r.Total}");
                    return 0;
                }
                case "cpp-goto-def":
                {
                    var nonOpts = args.Where(a => !a.StartsWith("--") && a != "cpp-goto-def").ToList();
                    if (nonOpts.Count < 3) throw new InvalidOperationException("usage: cpp-goto-def <file> <line> <col>");
                    var r = await conn.Proxy.CppGotoDefinitionAsync(nonOpts[0], int.Parse(nonOpts[1]), int.Parse(nonOpts[2]), null, null, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "cpp-class-members":
                {
                    var nonOpts = args.Where(a => !a.StartsWith("--") && a != "cpp-class-members").ToList();
                    if (nonOpts.Count < 2) throw new InvalidOperationException("usage: cpp-class-members <file> <className>");
                    var r = await conn.Proxy.CppClassMembersAsync(nonOpts[0], nonOpts[1], default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Error.WriteLine($"[cpp-class-members] file={r.File} class={r.ClassName} members={r.Members.Count}");
                    return 0;
                }
                case "file-outline":
                {
                    var file = args.LastOrDefault(a => !a.StartsWith("--") && a != "file-outline")
                        ?? throw new InvalidOperationException("provide file path");
                    var r = await conn.Proxy.FileOutlineAsync(file, default).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "move-methods":
                {
                    var jsonPath = args.LastOrDefault(a => !a.StartsWith("--") && a != "move-methods")
                        ?? throw new InvalidOperationException("provide moves.json path");
                    var json = File.ReadAllText(jsonPath);
                    var moves = JsonSerializer.Deserialize<List<MoveMethodRequest>>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new InvalidOperationException("empty moves list");

                    Console.Error.WriteLine($"[move-methods] count={moves.Count}");
                    int ok = 0, fail = 0;
                    foreach (var m in moves)
                    {
                        try
                        {
                            var r = await conn.Proxy.EditMoveMethodAsync(m.File, m.MethodName, m.ContainerType, m.NewFile, m.AppendIfExists)
                                              .ConfigureAwait(false);
                            if (r.Success) { ok++; Console.Error.WriteLine($"  ok    {m.MethodName,-32} -> {m.NewFile}"); }
                            else if (r.Conflict) { fail++; Console.Error.WriteLine($"  CONFL {m.MethodName,-32} -> {m.NewFile}  (set appendIfExists)"); }
                            else { fail++; Console.Error.WriteLine($"  FAIL  {m.MethodName,-32} -> {m.NewFile}  (member not found in container)"); }
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            Console.Error.WriteLine($"  THROW {m.MethodName,-32} -> {m.NewFile}  ({ex.GetType().Name}: {ex.Message})");
                        }
                    }
                    Console.Error.WriteLine($"[move-methods] ok={ok} fail={fail}");
                    return fail == 0 ? 0 : 1;
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
        Console.WriteLine("vsmcp-rearch move-methods <moves.json> # [{File, MethodName, NewFile, ContainerType?, AppendIfExists?}]");
        Console.WriteLine("vsmcp-rearch load-folder [rootPath]    # Open-Folder-mode csproj autoload");
        Console.WriteLine("vsmcp-rearch sidecar-status            # what's loaded in the sidecar workspace");
        Console.WriteLine("vsmcp-rearch find-symbol <name>        # CodeFindSymbol — sanity check after load-folder");
        Console.WriteLine("vsmcp-rearch file-outline <file>       # FileOutline — sanity check after load-folder");
    }

    public sealed class MoveTypeRequest
    {
        public string File { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string? NewFile { get; set; }
        public string? NewNamespace { get; set; }
        public bool AppendIfExists { get; set; }
    }

    public sealed class MoveMethodRequest
    {
        public string File { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string? ContainerType { get; set; }
        public string? NewFile { get; set; }
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
