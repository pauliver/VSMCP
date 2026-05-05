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
    /// Push the in-memory contents of an unsaved editor buffer into the analyzer's
    /// CXUnsavedFile table. Subsequent parses for any TU that #includes this file see
    /// <paramref name="content"/> instead of disk. Pass null/empty content to clear the
    /// override (revert to disk). Also drops the cached TU for this file so the next
    /// call reparses with the new content.
    /// </summary>
    Task SetUnsavedBufferAsync(string file, string? content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associate a precompiled-header source (e.g., "pch.h" / "stdafx.h") with a
    /// translation unit. The analyzer compiles the header to a clang-format .pch on
    /// first use and caches it in %TEMP%\vsmcp-pch\. Subsequent parses pass
    /// <c>-include-pch &lt;cached.pch&gt;</c> to skip re-parsing the header's include
    /// closure — order-of-magnitude speedup on large MFC/STL/Windows-SDK headers.
    /// Pass null pchHeader to clear. Drops the cached TU.
    /// </summary>
    Task SetPchHeaderAsync(string file, string? pchHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-TU find references: discover the canonical USR at (seedFile, line, column), then walk
    /// each file in <paramref name="additionalFiles"/> looking for cursors that resolve to the same
    /// USR. Returns aggregated locations across all walked TUs. Slow first-call; subsequent calls
    /// hit the cached TUs.
    /// </summary>
    Task<CppLocationListResult> FindReferencesInFilesAsync(string seedFile, int line, int column, string[] additionalFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
}
