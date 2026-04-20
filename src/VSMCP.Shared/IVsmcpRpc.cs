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
    Task<FocusResult> VsFocusAsync(CancellationToken cancellationToken = default);
    Task<AutoFocusResult> VsSetAutoFocusAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<AutoFocusResult> VsGetAutoFocusAsync(CancellationToken cancellationToken = default);

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
    Task<DebugActionResult> DebugStopCommandAsync(CancellationToken cancellationToken = default);
    Task<DebugActionResult> DebugKillAndStopAsync(CancellationToken cancellationToken = default);
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

    // -------- Breakpoints --------
    Task<BreakpointInfo> BreakpointSetAsync(BreakpointSetOptions options, CancellationToken cancellationToken = default);
    Task<BreakpointListResult> BreakpointListAsync(CancellationToken cancellationToken = default);
    Task BreakpointRemoveAsync(string bpId, CancellationToken cancellationToken = default);
    Task<BreakpointInfo> BreakpointEnableAsync(string bpId, CancellationToken cancellationToken = default);
    Task<BreakpointInfo> BreakpointDisableAsync(string bpId, CancellationToken cancellationToken = default);

    // -------- Inspection: threads, stacks, frames, eval --------
    Task<ThreadListResult> ThreadsListAsync(CancellationToken cancellationToken = default);
    Task<ThreadInfo> ThreadsFreezeAsync(int threadId, CancellationToken cancellationToken = default);
    Task<ThreadInfo> ThreadsThawAsync(int threadId, CancellationToken cancellationToken = default);
    Task<ThreadInfo> ThreadsSwitchAsync(int threadId, CancellationToken cancellationToken = default);
    Task<StackGetResult> StackGetAsync(int? threadId, int? depth, CancellationToken cancellationToken = default);
    Task<StackFrameInfo> FrameSwitchAsync(int? threadId, int frameIndex, CancellationToken cancellationToken = default);
    Task<VariableListResult> FrameLocalsAsync(int? threadId, int? frameIndex, int expandDepth, CancellationToken cancellationToken = default);
    Task<VariableListResult> FrameArgumentsAsync(int? threadId, int? frameIndex, int expandDepth, CancellationToken cancellationToken = default);
    Task<EvalResult> EvalExpressionAsync(EvalOptions options, CancellationToken cancellationToken = default);

    // -------- Inspection: modules & symbols --------
    Task<ModuleListResult> ModulesListAsync(CancellationToken cancellationToken = default);
    Task<SymbolStatusResult> SymbolsLoadAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<SymbolStatusResult> SymbolsStatusAsync(string moduleId, CancellationToken cancellationToken = default);

    // -------- Inspection: memory, registers, disasm --------
    Task<MemoryReadResult> MemoryReadAsync(string address, int length, CancellationToken cancellationToken = default);
    Task<MemoryWriteResult> MemoryWriteAsync(string address, string hex, bool allowSideEffects, CancellationToken cancellationToken = default);
    Task<RegistersResult> RegistersGetAsync(int? threadId, int? frameIndex, CancellationToken cancellationToken = default);
    Task<DisasmResult> DisasmGetAsync(string address, int count, CancellationToken cancellationToken = default);

    // -------- Crash-dump analysis --------
    Task<DumpOpenResult> DumpOpenAsync(DumpOpenOptions options, CancellationToken cancellationToken = default);
    Task<DumpSummaryResult> DumpSummaryAsync(CancellationToken cancellationToken = default);
    Task<DumpSaveResult> DumpSaveAsync(DumpSaveOptions options, CancellationToken cancellationToken = default);

    // -------- Diagnostics (counters + process enumeration) --------
    Task<ProcessListResult> ProcessesListAsync(ProcessListFilter? filter, CancellationToken cancellationToken = default);
    Task<CountersSnapshot> CountersGetAsync(int pid, int sampleMs, CancellationToken cancellationToken = default);

    // -------- Code intelligence (Roslyn) --------
    Task<SymbolsResult> CodeSymbolsAsync(string file, CancellationToken cancellationToken = default);
    Task<LocationListResult> CodeGotoDefinitionAsync(CodePosition position, CancellationToken cancellationToken = default);
    Task<ReferencesResult> CodeFindReferencesAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default);
    Task<DiagnosticsResult> CodeDiagnosticsAsync(string? file, int maxResults, CancellationToken cancellationToken = default);
    Task<QuickInfoResult> CodeQuickInfoAsync(CodePosition position, CancellationToken cancellationToken = default);
}
