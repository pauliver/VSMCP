using System.Threading;
using System.Threading.Tasks;

namespace VSMCP.Shared;

/// <summary>
/// JSON-RPC contract between VSMCP.Vsix and the out-of-process VSMCP.CppAnalyzer
/// sidecar. Lives on a separate pipe (`VSMCP.Cpp.&lt;vsPid&gt;`) and is intentionally
/// distinct from <see cref="IVsmcpRpc"/> so the analyzer's lifecycle is isolated.
/// </summary>
public interface IVsmcpCppRpc
{
    Task<PingResult> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Diagnose a single file. Returns parser errors/warnings; does not throw on parse failure.</summary>
    Task<CppDiagnosticsResult> DiagnosticsAsync(string file, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);

    /// <summary>Find references to the symbol at (line, column) within the same translation unit. Single-TU in v1.</summary>
    Task<CppLocationListResult> FindReferencesAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);

    /// <summary>Type/declaration text for the symbol at (line, column).</summary>
    Task<CppQuickInfoResult> QuickInfoAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);

    /// <summary>Definition site for the symbol at (line, column).</summary>
    Task<CppLocationResult> GotoDefinitionAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);

    /// <summary>Drop the cached translation unit for a file so the next query reparses (for editor-saves / source changes).</summary>
    Task InvalidateAsync(string file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-TU find references: discover the canonical USR at (seedFile, line, column), then walk
    /// each file in <paramref name="additionalFiles"/> looking for cursors that resolve to the same
    /// USR. Returns aggregated locations across all walked TUs. Slow first-call; subsequent calls
    /// hit the cached TUs.
    /// </summary>
    Task<CppLocationListResult> FindReferencesInFilesAsync(string seedFile, int line, int column, string[] additionalFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
}
