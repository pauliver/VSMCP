using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.index_find")]
    [Description("Solution-wide C++ symbol lookup backed by a persistent syntactic index. Case-insensitive substring match on declaration name, optional kind filter (class/struct/union/enum/typedef/using/function/method/namespace). Builds the index lazily on first use; unbounded by file count. Use cpp.index_rebuild after large external edits.")]
    public async Task<CppIndexResult> CppIndexFind(
        [Description("Name substring to match (empty matches all).")] string name,
        [Description("Optional kind filter. Omit for any.")] string? kind = null,
        [Description("Max entries (1..5000, default 200).")] int maxResults = 200,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppIndexFindAsync(name, kind, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.index_rebuild")]
    [Description("Rebuild the solution-wide C++ symbol index from scratch and return its size (file + symbol counts). Call after files change on disk outside the editor.")]
    public async Task<CppIndexStatsResult> CppIndexRebuild(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppIndexRebuildAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.classes")]
    [Description("Solution-wide aggregation of C/C++ class/struct/union/enum declarations. Walks every .h/.hpp/.cpp/.cc/.cxx in the solution, runs cpp_outline on each, and returns matching declarations with file + line + container chain. Cheaper than cpp_outline-per-file when you want a global view.")]
    public async Task<CppClassesResult> CppClasses(
        [Description("Glob name filter (e.g. 'Bus*'). Omit to return all.")] string? namePattern = null,
        [Description("Kinds to include. Default: class, struct, union, enum.")] IReadOnlyList<string>? kinds = null,
        [Description("Max results (default 1000).")] int maxResults = 1000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppClassesAsync(namePattern, kinds, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.find_symbol")]
    [Description("Cross-file C/C++ symbol search by name + optional kind. Returns the full Container chain (e.g. Z::Detail::MessageQueue::SlotAt) so an LLM can disambiguate matches without grepping. Like code_find_symbol but for C++. Tokenizer-based — see cpp_outline for parser limits.")]
    public async Task<CppFindSymbolResult> CppFindSymbol(
        [Description("Name to find (exact, or glob with * and ?).")] string name,
        [Description("Optional kind filter: namespace, class, struct, union, enum, typedef, using, function, method.")] string? kind = null,
        [Description("Max results (default 100).")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppFindSymbolAsync(name, kind, maxResults, ct).ConfigureAwait(false);
    }
}
