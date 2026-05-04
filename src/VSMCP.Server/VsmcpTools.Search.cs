using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "search.text")]
    [Description("Regex search across solution-tracked files. Returns matches with line/column and 2 lines of context before/after. Filter by file glob and project. Use code.find_references for semantic searches; use this for free-text or non-Roslyn languages.")]
    public async Task<TextSearchResult> SearchText(
        [Description("Regex pattern.")] string pattern,
        [Description("Glob pattern for files to include (e.g. '*.cs', '**/*.{ts,tsx}').")] string? filePattern = null,
        [Description("Project to scope. Omit for whole solution.")] string? projectId = null,
        [Description("Item kinds: 'file' (default).")] IReadOnlyList<string>? kinds = null,
        [Description("Max matches (default 500).")] int maxResults = 500,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchTextAsync(pattern, filePattern, projectId, kinds, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search.symbol")]
    [Description("Find symbols (any kind) matching a name pattern across the solution. Equivalent to VS's Ctrl+T 'Go to Symbol'. Pattern supports glob (* ?). Filter by symbol kind and container namespace/type.")]
    public async Task<SymbolSearchResultContainer> SearchSymbol(
        [Description("Symbol name pattern (glob).")] string namePattern,
        [Description("Symbol kinds (e.g. 'method', 'property', 'namedtype'). Omit for all.")] IReadOnlyList<string>? kinds = null,
        [Description("Container display string to filter (e.g. 'MyApp.Models').")] string? container = null,
        [Description("Max results (default 500).")] int maxResults = 500,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchSymbolAsync(namePattern, kinds, container, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search.classes")]
    [Description("Find classes/structs/interfaces in the solution. Filter by name pattern, by base type, or by implemented interface. Each result includes location, base type, and implemented interfaces.")]
    public async Task<ClassSearchResultContainer> SearchClasses(
        [Description("Name pattern (glob). Omit to match all.")] string? namePattern = null,
        [Description("Base type name to filter by (matches simple name or full display).")] string? baseType = null,
        [Description("Interface name to filter by.")] string? @interface = null,
        [Description("Max results (default 500).")] int maxResults = 500,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchClassesAsync(namePattern, baseType, @interface, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search.members")]
    [Description("Find members across the solution by name pattern. Optionally scope to a specific containing type. Returns kind, signature, location, and container.")]
    public async Task<MemberSearchResultContainer> SearchMembers(
        [Description("Member name pattern (glob).")] string namePattern,
        [Description("Member kinds: 'method', 'property', 'field', 'event'. Omit for all.")] IReadOnlyList<string>? kinds = null,
        [Description("Containing type display string to scope (e.g. 'MyApp.Models.User').")] string? container = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchMembersAsync(namePattern, kinds, container, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search.find_usages")]
    [Description("Find all usages (definitions + references) of the symbol at a given file/line/column. Backed by Roslyn SymbolFinder. Equivalent to 'Find All References' in VS.")]
    public async Task<UsageResult> SearchFindUsages(
        [Description("Absolute file path.")] string file,
        [Description("Position {line, column} of the symbol (1-based).")] CodePosition position,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchFindUsagesAsync(file, position, ct).ConfigureAwait(false);
    }
}
