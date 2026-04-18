using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VSMCP.Shared;

/// <summary>
/// JSON-RPC contract implemented by the VSIX and called by VSMCP.Server.
/// Method names are stable — any breaking change bumps <see cref="ProtocolVersion"/>.
/// </summary>
public interface IVsmcpRpc
{
    // -------- Meta --------
    Task<HandshakeResult> HandshakeAsync(int clientMajor, int clientMinor, CancellationToken cancellationToken = default);
    Task<PingResult> PingAsync(CancellationToken cancellationToken = default);
    Task<VsStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    // -------- Solution --------
    Task<SolutionInfo> SolutionInfoAsync(CancellationToken cancellationToken = default);
    Task<SolutionInfo> SolutionOpenAsync(string path, CancellationToken cancellationToken = default);
    Task SolutionCloseAsync(bool saveFirst, CancellationToken cancellationToken = default);

    // -------- Project --------
    Task<IReadOnlyList<ProjectInfo>> ProjectListAsync(CancellationToken cancellationToken = default);
    Task<ProjectInfo> ProjectAddAsync(string templatePathOrExistingFile, string destinationPath, string? projectName, CancellationToken cancellationToken = default);
    Task ProjectRemoveAsync(string projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PropertyValue>> ProjectPropertiesGetAsync(string projectId, IReadOnlyList<string>? keys, CancellationToken cancellationToken = default);
    Task ProjectPropertiesSetAsync(string projectId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
    Task<ProjectItemRef> ProjectFileAddAsync(string projectId, string path, bool linkOnly, CancellationToken cancellationToken = default);
    Task ProjectFileRemoveAsync(string projectId, string path, bool deleteFromDisk, CancellationToken cancellationToken = default);
    Task<ProjectItemRef> ProjectFolderCreateAsync(string projectId, string path, CancellationToken cancellationToken = default);

    // -------- File / editor --------
    Task<FileReadResult> FileReadAsync(string path, FileRange? range, CancellationToken cancellationToken = default);
    Task<FileWriteResult> FileWriteAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<FileWriteResult> FileReplaceRangeAsync(string path, FileRange range, string text, CancellationToken cancellationToken = default);

    Task EditorOpenAsync(string path, int? line, int? column, CancellationToken cancellationToken = default);
    Task EditorSaveAsync(string path, CancellationToken cancellationToken = default);
    Task EditorSaveAllAsync(CancellationToken cancellationToken = default);

    // -------- Build --------
    Task<BuildHandle> BuildStartAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default);
    Task<BuildHandle> BuildRebuildAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default);
    Task<BuildHandle> BuildCleanAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default);
    Task<BuildStatus> BuildStatusAsync(string buildId, CancellationToken cancellationToken = default);
    Task<BuildStatus> BuildWaitAsync(string buildId, int? timeoutMs, CancellationToken cancellationToken = default);
    Task<BuildStatus> BuildCancelAsync(string buildId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildDiagnostic>> BuildErrorsAsync(string buildId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildDiagnostic>> BuildWarningsAsync(string buildId, CancellationToken cancellationToken = default);
    Task<BuildOutput> BuildOutputAsync(string buildId, string? pane, CancellationToken cancellationToken = default);

    // -------- Debug control --------
    Task<DebugActionResult> DebugLaunchAsync(DebugLaunchOptions options, CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugAttachAsync(DebugAttachOptions options, CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugStopAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugDetachAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugRestartAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugBreakAllAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugContinueAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugStepIntoAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugStepOverAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugStepOutAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugRunToCursorAsync(string file, int line, CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugSetNextStatementAsync(string file, int line, bool allowSideEffects, CancellationToken cancellationToken = default);
    Task<DebugInfo> DebugStateAsync(CancellationToken cancellationToken = default);
}
