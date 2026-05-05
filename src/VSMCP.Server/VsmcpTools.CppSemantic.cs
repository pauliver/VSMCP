using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.diagnostics")]
    [Description("Real libclang-backed diagnostics for a C/C++ file: errors, warnings, includes-not-found, etc. Spawns the VSMCP.CppAnalyzer sidecar on first call (cold-start ~1-3s, warm calls fast). v1 limitation: parses on-disk content; dirty editor buffers ignored.")]
    public async Task<CppDiagnosticsResult> CppDiagnostics(
        [Description("Absolute path to the C/C++ source/header file.")] string file,
        [Description("Extra include directories to pass as -I.")] string[]? extraIncludes = null,
        [Description("Extra preprocessor defines to pass as -D (e.g. 'WIN32', 'DEBUG=1').")] string[]? extraDefines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppDiagnosticsAsync(file, extraIncludes, extraDefines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.find_references")]
    [Description("Find references to the C/C++ symbol at (line, column) within the same translation unit. v1 is single-TU — solution-wide cross-TU is a v2 add. libclang-backed.")]
    public async Task<CppLocationListResult> CppFindReferencesSem(
        [Description("Absolute path to the C/C++ source/header.")] string file,
        [Description("1-based line of the symbol.")] int line,
        [Description("1-based column of the symbol.")] int column,
        [Description("Extra include dirs.")] string[]? extraIncludes = null,
        [Description("Extra defines.")] string[]? extraDefines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppFindReferencesSemAsync(file, line, column, extraIncludes, extraDefines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.quick_info")]
    [Description("Type/declaration text for the C/C++ symbol at (line, column). libclang-backed.")]
    public async Task<CppQuickInfoResult> CppQuickInfo(
        [Description("Absolute path to the C/C++ source/header.")] string file,
        [Description("1-based line of the symbol.")] int line,
        [Description("1-based column of the symbol.")] int column,
        [Description("Extra include dirs.")] string[]? extraIncludes = null,
        [Description("Extra defines.")] string[]? extraDefines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppQuickInfoAsync(file, line, column, extraIncludes, extraDefines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.goto_definition")]
    [Description("Definition site for the C/C++ symbol at (line, column). libclang-backed.")]
    public async Task<CppLocationResult> CppGotoDefinition(
        [Description("Absolute path to the C/C++ source/header.")] string file,
        [Description("1-based line of the symbol.")] int line,
        [Description("1-based column of the symbol.")] int column,
        [Description("Extra include dirs.")] string[]? extraIncludes = null,
        [Description("Extra defines.")] string[]? extraDefines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppGotoDefinitionAsync(file, line, column, extraIncludes, extraDefines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.invalidate")]
    [Description("Drop the cached CppAnalyzer translation unit for a file so the next semantic query reparses. Call after saving an edit so subsequent diagnostics/references reflect the new content.")]
    public async Task CppInvalidate(
        [Description("Absolute path to the C/C++ file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.CppInvalidateAsync(file, ct).ConfigureAwait(false);
    }
}
