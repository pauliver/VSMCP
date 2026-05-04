using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "file.write_many")]
    [Description("Write or replace ranges in many files in one call. Each entry can be a full-file write (Content) or a range replacement (Range + Text). Per-item errors are returned in BatchResult; successful items don't roll back on later failures.")]
    public async Task<BatchResult<FileWriteResultItem>> FileWriteMany(
        [Description("Write entries. Each: { Path, Content? } for full-file write, or { Path, Range, Text } for range replace.")] IReadOnlyList<FileWriteEntry> entries,
        [Description("Open each written file in the editor after writing. Default false.")] bool openInEditor = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileWriteManyAsync(entries, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search.replace_many")]
    [Description("Solution-wide regex replace. By default applies replacements; pass dryRun=true to count matches without writing. Filter target files by glob.")]
    public async Task<ReplaceManyResult> SearchReplaceMany(
        [Description("Regex pattern.")] string pattern,
        [Description("Replacement string (regex back-refs $1, $2, … supported).")] string replacement,
        [Description("File glob to scope (e.g. '*.cs').")] string? filePattern = null,
        [Description("Max files to process (default 1000).")] int maxFiles = 1000,
        [Description("True to count matches but not write. Default false.")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SearchReplaceManyAsync(pattern, replacement, filePattern, maxFiles, dryRun, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.symbols_many")]
    [Description("Run code.symbols on N files in one call. Returns one result per file in BatchResult ordering; per-file errors are isolated.")]
    public async Task<BatchResult<CodeBatchResult>> CodeSymbolsMany(
        [Description("Absolute file paths.")] IReadOnlyList<string> files,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeSymbolsManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.find_references_many")]
    [Description("Run code.find_references on N positions in one call. Useful when you have a list of identifier positions to check.")]
    public async Task<BatchResult<ReferencesResult>> CodeFindReferencesMany(
        [Description("Positions (file/line/column) to find references for.")] IReadOnlyList<CodePosition> positions,
        [Description("Per-position max results (default 1000).")] int maxResults = 1000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeFindReferencesManyAsync(positions, maxResults, ct).ConfigureAwait(false);
    }
}
