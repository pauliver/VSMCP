using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    // -------- Phase 1 --------

    [McpServerTool(Name = "build.summary")]
    [Description("Compact build result: per-project status + first error + total counts. Replaces the verbose build.output for the common 'did it build?' / 'first thing wrong' workflows. ~95% smaller than build.output.")]
    public async Task<BuildSummaryResult> BuildSummary(
        [Description("Build id from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildSummaryAsync(buildId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "test.run_summary")]
    [Description("Run tests and return only counts + failure details (no per-test 'Passed' spam). mode: 'summary' (default — counts + first 5 failures), 'failures' (all failures + tail of output), 'full' (current test.run output).")]
    public async Task<TestSummaryResult> TestRunSummary(
        [Description("vstest /TestCaseFilter expression. Omit to run all tests.")] string? filter = null,
        [Description("Project to scope.")] string? projectId = null,
        [Description("Configuration (Debug/Release).")] string? configuration = null,
        [Description("Output mode: 'summary' (default), 'failures', 'full'.")] string mode = "summary",
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.TestRunSummaryAsync(filter, projectId, configuration, mode, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.diagnostics_grouped")]
    [Description("Roslyn diagnostics grouped by file with compact per-diagnostic shape (id, line, column, identifier, brief message). Eliminates path repetition and trims boilerplate; typically 60-80% smaller than code.diagnostics.")]
    public async Task<GroupedDiagnosticsResult> CodeDiagnosticsGrouped(
        [Description("File path to scope. Omit for solution-wide.")] string? file = null,
        [Description("Max diagnostics (default 1000).")] int maxResults = 1000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeDiagnosticsGroupedAsync(file, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.errors_grouped")]
    [Description("Build errors + warnings grouped by file with compact per-diagnostic shape. Same idea as code.diagnostics_grouped but for the most recent build's diagnostics.")]
    public async Task<GroupedDiagnosticsResult> BuildErrorsGrouped(
        [Description("Build id from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildErrorsGroupedAsync(buildId, ct).ConfigureAwait(false);
    }

    // -------- Phase 2 --------

    [McpServerTool(Name = "file.read_if_changed")]
    [Description("Read a file only if its content hash differs from the supplied knownHash. When unchanged, returns ~30 tokens instead of the full content. Use this in iterative tasks where you want to re-check a file you've already read.")]
    public async Task<FileReadIfChangedResult> FileReadIfChanged(
        [Description("Absolute file path.")] string path,
        [Description("Hash of the content you already have (from a prior FileRead/FileReadIfChanged response). Empty/null forces a full read.")] string? knownHash = null,
        [Description("Optional range (line/column-bounded) — applies to the returned content, not the hash check.")] FileRange? range = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileReadIfChangedAsync(path, knownHash, range, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.diff")]
    [Description("Diff a file's current content against either git HEAD (when baseHash is null and the file lives in a git repo) or against an explicit content hash you previously held. Returns hunks (start line + removed/added lines), not the whole file. ~95% smaller for small edits in big files.")]
    public async Task<CodeDiffResult> CodeDiff(
        [Description("Absolute file path.")] string file,
        [Description("Content hash from an earlier FileRead. Omit to diff against git HEAD.")] string? baseHash = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeDiffAsync(file, baseHash, ct).ConfigureAwait(false);
    }

    // -------- Phase 3 --------

    [McpServerTool(Name = "file.outline")]
    [Description("Return a file's shape (usings, namespaces, types, member signatures) without method bodies. Each member entry includes its line number so a follow-up code.read_member can target it. Typically 80-95% smaller than file.read for files >50 lines.")]
    public async Task<FileOutlineResult> FileOutline(
        [Description("Absolute file path.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileOutlineAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.info")]
    [Description("Return metadata about a file without its content: language, encoding, line count, byte size, content hash, owning project, namespace, IsTest/IsGenerated heuristics, outline depth, and whether it's open in the editor with unsaved changes. ~50 tokens vs ~5000+ for a full file.read.")]
    public async Task<FileInfoResult> FileInfo(
        [Description("Absolute file path.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileInfoAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.symbol_summary")]
    [Description("Lightweight 'what does this thing do' summary: signature + the methods it calls + the fields it touches + the exception types it throws + cyclomatic complexity + line count + IsAsync/Returns. No body — call code.read_member or code.investigate when you actually need the source.")]
    public async Task<SymbolSummaryResult> CodeSymbolSummary(
        [Description("Symbol name. Use 'Class.Member' for qualified lookup.")] string symbol,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeSymbolSummaryAsync(symbol, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.investigate")]
    [Description("One-call symbol exploration: returns definition + body + callers + invocations + tests heuristic + stats (IsAsync/IsStatic/IsAbstract/IsVirtual/ReferenceCount). Replaces a 5-step chain (find_symbol + read_member + find_references + per-file reads + test grep).")]
    public async Task<InvestigateResult> CodeInvestigate(
        [Description("Symbol name (qualified or simple).")] string symbol,
        [Description("Max references to return (default 50).")] int maxRefs = 50,
        [Description("Include tests-that-touch-this heuristic (containing type name contains Test/Spec). Default true.")] bool includeTests = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeInvestigateAsync(symbol, maxRefs, includeTests, ct).ConfigureAwait(false);
    }

    // -------- Phase 4 --------

    [McpServerTool(Name = "session.scope")]
    [Description("Declare a working set of symbols / project / folder for subsequent calls. While a scope is active, symbol-resolution tools prefer matches inside the scope, reducing SymbolAmbiguous errors and letting you omit redundant params.")]
    public async Task<SessionScopeResult> SessionScope(
        [Description("Symbol names you're working on (e.g. 'AuthService', 'LoginAsync').")] IReadOnlyList<string>? symbols = null,
        [Description("Project to scope to.")] string? project = null,
        [Description("Folder under the project to scope to.")] string? folder = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SessionScopeAsync(symbols, project, folder, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "session.current")]
    [Description("Return the currently active session scope (if any). Active=false when no scope is set.")]
    public async Task<SessionCurrentResult> SessionCurrent(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SessionCurrentAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "session.clear")]
    [Description("Clear the active session scope. Subsequent symbol-resolution tools fall back to whole-solution search.")]
    public async Task SessionClear(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.SessionClearAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "io.context")]
    [Description("One-call snapshot of the IDE state: active editor (path/language/cursor/dirty), solution status, debugger state, and last build outcome. Replaces ~4 separate calls (editor.active + vs.status + debug.state + ad-hoc) at the start of a task.")]
    public async Task<IoContextResult> IoContext(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.IoContextAsync(ct).ConfigureAwait(false);
    }
}
