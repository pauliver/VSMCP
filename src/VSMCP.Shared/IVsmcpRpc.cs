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

    // Open Folder mode sidecar — load loose .csprojs into an in-process workspace so
    // Roslyn-aware tools (file_outline, code_find_symbol, etc.) work even without a .sln.
    Task<CppOutlineResult> CppOutlineAsync(string file, CancellationToken cancellationToken = default);
    Task<CppMembersResult> CppClassMembersAsync(string file, string className, CancellationToken cancellationToken = default);
    Task<CppClassesResult> CppClassesAsync(string? namePattern, IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default);
    Task<CppFindSymbolResult> CppFindSymbolAsync(string name, string? kind, int maxResults, CancellationToken cancellationToken = default);

    // Semantic tier — proxied to the out-of-process CppAnalyzer (libclang).
    Task<CppDiagnosticsResult> CppDiagnosticsAsync(string file, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
    Task<CppLocationListResult> CppFindReferencesSemAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
    Task<CppQuickInfoResult> CppQuickInfoAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
    Task<CppLocationResult> CppGotoDefinitionAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
    Task CppInvalidateAsync(string file, CancellationToken cancellationToken = default);
    /// <summary>Push or clear an unsaved-buffer override for libclang. Pass null/empty to revert to disk.</summary>
    Task CppSetUnsavedBufferAsync(string file, string? content, CancellationToken cancellationToken = default);
    /// <summary>Set or clear a precompiled-header source for a file. The analyzer compiles + caches the PCH on first use.</summary>
    Task CppSetPchHeaderAsync(string file, string? pchHeader, CancellationToken cancellationToken = default);
    Task<CppReadMemberResult> CppReadMemberAsync(string file, string className, string memberName, CancellationToken cancellationToken = default);
    Task<CppOrganizeIncludesResult> CppOrganizeIncludesAsync(string file, CancellationToken cancellationToken = default);
    Task<CppSymbolSummaryResult> CppSymbolSummaryAsync(string symbol, CancellationToken cancellationToken = default);
    Task<CppReplaceMemberResult> CppReplaceMemberAsync(string file, string className, string memberName, string newCode, CancellationToken cancellationToken = default);
    Task<CppGenerateCtorResult> CppGenerateConstructorAsync(string file, string className, IReadOnlyList<string>? memberNames, CancellationToken cancellationToken = default);
    Task<CppOverrideMemberResult> CppOverrideMemberAsync(string file, string className, string methodName, string returnType, string parameters, CancellationToken cancellationToken = default);
    Task<CppLocationListResult> CppFindReferencesSolutionAsync(string file, int line, int column, int maxFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default);
    Task<CppLocationListResult> CppRenameAsync(string file, int line, int column, string newName, CancellationToken cancellationToken = default);
    Task<CppInvestigateResult> CppInvestigateAsync(string symbol, int maxRefs, CancellationToken cancellationToken = default);
    Task<CppOutlineManyResult> CppOutlineManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<CppInheritanceResult> CppInheritanceAsync(string file, string className, int maxDepth, CancellationToken cancellationToken = default);
    Task<CppGenerateEqualityResult> CppGenerateEqualityAsync(string file, string className, CancellationToken cancellationToken = default);
    Task<CppImplementInterfaceResult> CppImplementInterfaceAsync(string derivedFile, string derivedClass, string baseClass, string? baseClassFile, CancellationToken cancellationToken = default);
    Task<CppScaffoldFileResult> CppScaffoldFileAsync(string headerPath, string className, string? @namespace, bool createCpp, CancellationToken cancellationToken = default);
    Task<CppSuggestIncludesResult> CppSuggestIncludesAsync(string symbol, CancellationToken cancellationToken = default);
    Task<CppRenameSolutionResult> CppRenameSolutionAsync(string file, int line, int column, string newName, int maxFiles, CancellationToken cancellationToken = default);
    Task<CppMoveTypeResult> CppMoveTypeAsync(string sourceFile, string typeName, string targetFile, bool createTargetIfMissing, CancellationToken cancellationToken = default);
    Task<CppMoveMethodResult> CppMoveMethodAsync(string sourceFile, string className, string methodName, string targetFile, bool createTargetIfMissing, CancellationToken cancellationToken = default);
    Task<CppAnalyzerStatusResult> CppAnalyzerStatusAsync(int recentLogLines, CancellationToken cancellationToken = default);
    Task<CppIndexResult> CppIndexFindAsync(string name, string? kind, int maxResults, CancellationToken cancellationToken = default);
    Task<CppIndexStatsResult> CppIndexRebuildAsync(CancellationToken cancellationToken = default);

    Task<ProjectLoadResult> ProjectLoadAsync(string csprojPath, CancellationToken cancellationToken = default);
    Task<ProjectLoadFolderResult> ProjectLoadWorkspaceFolderAsync(string? rootPath, CancellationToken cancellationToken = default);
    Task<SidecarStatusResult> ProjectSidecarStatusAsync(CancellationToken cancellationToken = default);

    // -------- File / editor --------
    Task<FileReadResult> FileReadAsync(string path, FileRange? range, CancellationToken cancellationToken = default);
    Task<FileWriteResult> FileWriteAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<FileWriteResult> FileReplaceRangeAsync(string path, FileRange range, string text, CancellationToken cancellationToken = default);

    Task EditorOpenAsync(string path, int? line, int? column, CancellationToken cancellationToken = default);
    Task EditorSaveAsync(string path, CancellationToken cancellationToken = default);
    Task EditorSaveAllAsync(CancellationToken cancellationToken = default);

    // Bulk file rename / move with optional .csproj <Compile Include> sync. dryRun returns
    // the planned outcomes without touching disk or the csproj.
    Task<FileMoveManyResult> FileMoveManyAsync(IReadOnlyList<FileMovePair> moves, bool updateProject, bool dryRun, CancellationToken cancellationToken = default);

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
    Task<SetVariableResult> DebugSetVariableAsync(string name, string value, CancellationToken cancellationToken = default);

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

    // -------- Diagnostic Tools (events, memory, CPU) --------
    Task<DiagEventsResult> DiagEventsListAsync(string? filter, int maxResults, CancellationToken cancellationToken = default);
    Task<DiagEventsResult> DiagEventsWatchAsync(string? filter, int maxResults, long sinceTimestampMs, int timeoutMs, CancellationToken cancellationToken = default);
    Task<DiagEventDetail> DiagEventDetailAsync(string eventId, CancellationToken cancellationToken = default);
    Task DiagEventsClearAsync(CancellationToken cancellationToken = default);
    Task<DiagMemorySnapshot> DiagMemorySnapshotAsync(CancellationToken cancellationToken = default);
    Task<DiagCpuTimelineResult> DiagCpuTimelineAsync(int? windowMs, CancellationToken cancellationToken = default);

    // -------- Code intelligence (Roslyn) --------
    Task<SymbolsResult> CodeSymbolsAsync(string file, CancellationToken cancellationToken = default);
    Task<LocationListResult> CodeGotoDefinitionAsync(CodePosition position, CancellationToken cancellationToken = default);
    Task<ReferencesResult> CodeFindReferencesAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default);
    Task<DiagnosticsResult> CodeDiagnosticsAsync(string? file, int maxResults, CancellationToken cancellationToken = default);
    Task<QuickInfoResult> CodeQuickInfoAsync(CodePosition position, CancellationToken cancellationToken = default);
    Task<FormatResult> CodeFormatAsync(string file, FileRange? range, CancellationToken cancellationToken = default);
    Task<CompletionResult> CodeCompleteAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default);
    Task<SignatureHelpResult> CodeSignatureHelpAsync(CodePosition position, CancellationToken cancellationToken = default);
    Task<CallHierarchyResult> CodeCallHierarchyAsync(CodePosition position, string direction, int maxResults, CancellationToken cancellationToken = default);
    Task<TypeSurfaceResult> CodeTypeSurfaceAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default);
    Task<ListFixesResult> CodeListFixesAsync(CodePosition position, CancellationToken cancellationToken = default);
    Task<ApplyFixResult> CodeApplyFixAsync(CodePosition position, string? title, CancellationToken cancellationToken = default);

    // -------- M12: File & Symbol Discovery --------
    Task<FileListResult> FileListAsync(string? projectId, string? folder, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default);
    Task<ClassesResult> FileClassesAsync(string? projectId, string? @namespace, IReadOnlyList<string>? kinds,
        int maxResults, CancellationToken cancellationToken = default);
    Task<MembersResult> FileMembersAsync(string file, string className, IReadOnlyList<string>? kinds, bool excludeInherited,
        CancellationToken cancellationToken = default);
    Task<InheritanceResult> FileInheritanceAsync(string file, string className, CancellationToken cancellationToken = default);
    Task<FileListResult> FileGlobAsync(IReadOnlyList<string> patterns, string? projectId, CancellationToken cancellationToken = default);
    Task<DependencyListResult> FileDependenciesAsync(string file, CancellationToken cancellationToken = default);

    // -------- M13: Search Operations --------
    Task<TextSearchResult> SearchTextAsync(string pattern, string? filePattern, string? projectId,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default);
    Task<SymbolSearchResultContainer> SearchSymbolAsync(string namePattern, IReadOnlyList<string>? kinds,
        string? container, int maxResults, CancellationToken cancellationToken = default);
    Task<ClassSearchResultContainer> SearchClassesAsync(string? namePattern, string? baseType, string? @interface,
        int maxResults, CancellationToken cancellationToken = default);
    Task<MemberSearchResultContainer> SearchMembersAsync(string namePattern, IReadOnlyList<string>? kinds,
        string? container, CancellationToken cancellationToken = default);
    Task<UsageResult> SearchFindUsagesAsync(string file, CodePosition position, CancellationToken cancellationToken = default);

    // -------- M14: Bulk Operations --------
    Task<BatchResult<FileReadResultItem>> FileReadManyAsync(IReadOnlyList<FileReadRequest> requests,
        CancellationToken cancellationToken = default);
    Task<BatchResult<FileWriteResultItem>> FileWriteManyAsync(IReadOnlyList<FileWriteEntry> entries,
        bool openInEditor, CancellationToken cancellationToken = default);
    Task<ReplaceManyResult> SearchReplaceManyAsync(string pattern, string replacement, string? filePattern,
        int maxFiles, bool dryRun, CancellationToken cancellationToken = default);
    Task<BatchResult<CodeBatchResult>> CodeSymbolsManyAsync(IReadOnlyList<string> files,
        CancellationToken cancellationToken = default);
    Task<BatchResult<ReferencesResult>> CodeFindReferencesManyAsync(IReadOnlyList<CodePosition> positions,
        int maxResults, CancellationToken cancellationToken = default);

    // -------- Issue #122: _many batch variants for class/method/edit tools --------

    // C++ batches
    Task<BatchResult<CppReadMemberResult>> CppReadMemberManyAsync(IReadOnlyList<CppReadMemberItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppReplaceMemberResult>> CppReplaceMemberManyAsync(IReadOnlyList<CppReplaceMemberItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppOrganizeIncludesResult>> CppOrganizeIncludesManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<BatchResult<bool>> CppSetUnsavedBufferManyAsync(IReadOnlyList<CppUnsavedBufferItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<bool>> CppInvalidateManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<BatchResult<CppLocationListResult>> CppRenameManyAsync(IReadOnlyList<CppRenameItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppMoveTypeResult>> CppMoveTypeManyAsync(IReadOnlyList<CppMoveTypeItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppMoveMethodResult>> CppMoveMethodManyAsync(IReadOnlyList<CppMoveMethodItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppImplementInterfaceResult>> CppImplementInterfaceManyAsync(IReadOnlyList<CppImplementInterfaceItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppGenerateCtorResult>> CppGenerateConstructorManyAsync(IReadOnlyList<CppGenerateCtorItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppOverrideMemberResult>> CppOverrideMemberManyAsync(IReadOnlyList<CppOverrideMemberItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppGenerateEqualityResult>> CppGenerateEqualityManyAsync(IReadOnlyList<CppGenerateEqualityItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppQuickInfoResult>> CppQuickInfoManyAsync(IReadOnlyList<CppPositionItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppLocationListResult>> CppFindReferencesManyAsync(IReadOnlyList<CppPositionItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<CppLocationResult>> CppGotoDefinitionManyAsync(IReadOnlyList<CppPositionItem> items, CancellationToken cancellationToken = default);

    // C# batches
    Task<BatchResult<ReplaceMemberResult>> EditReplaceMemberManyAsync(IReadOnlyList<EditReplaceMemberItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<MoveTypeResult>> EditMoveTypeManyAsync(IReadOnlyList<EditMoveTypeItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<MoveTypeResult>> EditMoveMethodManyAsync(IReadOnlyList<EditMoveMethodItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<RenameResult>> EditRenameManyAsync(IReadOnlyList<EditRenameItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<AddUsingResult>> EditAddUsingManyAsync(IReadOnlyList<EditAddUsingItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<AddIncludeResult>> EditAddIncludeManyAsync(IReadOnlyList<EditAddIncludeItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<OrganizeUsingsResult>> EditOrganizeUsingsManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<BatchResult<AddMemberResult>> CodeImplementInterfaceManyAsync(IReadOnlyList<CodeImplementInterfaceItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<AddMemberResult>> CodeOverrideMemberManyAsync(IReadOnlyList<CodeOverrideMemberItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<AddMemberResult>> CodeGenerateConstructorManyAsync(IReadOnlyList<CodeGenerateCtorItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<AddMemberResult>> CodeGenerateEqualityManyAsync(IReadOnlyList<CodeGenerateEqualityItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<QuickInfoResult>> CodeQuickInfoManyAsync(IReadOnlyList<CodePositionItem> items, CancellationToken cancellationToken = default);

    // File batches (file.classes_many intentionally omitted: file.classes is project-wide,
    // not per-file, so a list-of-files shape is meaningless.)
    Task<BatchResult<MembersResult>> FileMembersManyAsync(IReadOnlyList<FileMembersItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<FileInfoResult>> FileInfoManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<BatchResult<FileReadIfChangedResult>> FileReadIfChangedManyAsync(IReadOnlyList<FileReadIfChangedItem> items, CancellationToken cancellationToken = default);
    Task<BatchResult<FileOutlineResult>> FileOutlineManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);

    // -------- M15: Refactoring & Editing --------
    Task<(int Replacements, string Text)> EditReplaceAllAsync(string file, string pattern, string replacement,
        int? maxReplacements, bool regex, CancellationToken cancellationToken = default);
    Task<RenameResult> EditRenameAsync(string file, CodePosition position, string newName, bool dryRun,
        CancellationToken cancellationToken = default);
    Task<OrganizeUsingsResult> EditOrganizeUsingsAsync(string file, bool addMissing, bool removeUnused,
        CancellationToken cancellationToken = default);
    Task<InsertResult> EditInsertBeforeAsync(string file, int line, string text, bool openInEditor,
        CancellationToken cancellationToken = default);
    Task<InsertResult> EditInsertAfterAsync(string file, int line, string text, bool openInEditor,
        CancellationToken cancellationToken = default);
    Task<ReplaceMemberResult> EditReplaceMemberAsync(string file, string className, string memberName, string newText,
        bool openInEditor, CancellationToken cancellationToken = default);
    Task<MoveTypeResult> EditMoveTypeAsync(string file, string typeName, string? newNamespace, string? newFile,
        bool appendIfExists, CancellationToken cancellationToken = default);
    Task<MoveTypeResult> EditMoveMethodAsync(string file, string methodName, string? containerType, string? newFile,
        bool appendIfExists, CancellationToken cancellationToken = default);
    Task<ApplyPatchResult> EditApplyPatchAsync(string unifiedDiff, bool dryRun, CancellationToken cancellationToken = default);

    // -------- M16: Navigation Context --------
    Task<NavigateResult> EditorNavigateToAsync(string file, int? line, int? column, bool openInEditor,
        CancellationToken cancellationToken = default);
    Task<SnippetResult> EditorSnippetAsync(string file, int line, int contextBefore, int contextAfter,
        CancellationToken cancellationToken = default);
    Task<RegionResult> EditorExpandRegionAsync(string file, int line, CancellationToken cancellationToken = default);
    Task<RegionResult> EditorCollapseRegionAsync(string file, int line, CancellationToken cancellationToken = default);
    Task<IncludeNavigationResult> EditorNavigateToIncludeAsync(string file, string includeName,
        CancellationToken cancellationToken = default);

    // -------- M18: Semantic Code Layer --------
    Task<SymbolMatchResult> CodeFindSymbolAsync(string name, string? kind, int maxResults, CancellationToken cancellationToken = default);
    Task<ReadMemberResult> CodeReadMemberAsync(string? file, string className, string memberName, CancellationToken cancellationToken = default);
    Task<AddMemberResult> EditAddMemberAsync(string? file, string className, string memberCode, string? insertBefore, bool openInEditor, CancellationToken cancellationToken = default);
    Task<AddUsingResult> EditAddUsingAsync(string file, string namespaceName, CancellationToken cancellationToken = default);
    Task<RemoveUsingResult> EditRemoveUsingAsync(string file, string namespaceName, CancellationToken cancellationToken = default);
    Task<UsingSuggestionsResult> CodeSuggestUsingsAsync(string file, IReadOnlyList<string>? symbolNames, CancellationToken cancellationToken = default);
    Task<AddIncludeResult> EditAddIncludeAsync(string file, string headerPath, bool isSystem, CancellationToken cancellationToken = default);
    Task<NamespaceInfo> ProjectNamespaceForPathAsync(string projectId, string relativePath, CancellationToken cancellationToken = default);
    Task<ScaffoldResult> CodeScaffoldFileAsync(string projectId, string relativePath, string? content, string? language, CancellationToken cancellationToken = default);
    Task<CreateClassResult> CodeCreateClassAsync(string name, string? baseClass, IReadOnlyList<string>? interfaces, string? projectId, string? folder, bool generateStubs, CancellationToken cancellationToken = default);
    Task<CppCreateClassResult> CppCreateClassAsync(string name, string? baseClass, string? headerFolder, string? sourceFolder, string? projectId, bool generateVirtualStubs, CancellationToken cancellationToken = default);
    Task<NavigateResult> EditorOpenAtSymbolAsync(string symbolPath, string? kind, CancellationToken cancellationToken = default);
    Task<BreakpointInfo> BreakpointSetOnEntryAsync(string className, string methodName, string? condition, CancellationToken cancellationToken = default);

    // -------- C++ Extensions --------
    Task<HeaderLookupResult> CppHeaderLookupAsync(string file, string symbolName,
        CancellationToken cancellationToken = default);
    Task<IncludeChainResult> CppIncludeChainAsync(string file, CancellationToken cancellationToken = default);
    Task<MacroResult> CppMacroLookupAsync(string name, CancellationToken cancellationToken = default);
    Task<PreprocessResult> CppPreprocessAsync(string file, IReadOnlyList<string>? defines,
        CancellationToken cancellationToken = default);
    Task<ApiReferenceResult> CppApiRefAsync(string apiName, CancellationToken cancellationToken = default);
    Task<GeneratedFileInfo> CppGeneratedFileAsync(string file, string type, CancellationToken cancellationToken = default);

    // -------- Active editor surface --------
    Task<ActiveEditorInfo> EditorActiveAsync(CancellationToken cancellationToken = default);
    Task<EditorSelection?> EditorSelectionAsync(CancellationToken cancellationToken = default);
    Task<CodePosition?> EditorCursorAsync(CancellationToken cancellationToken = default);
    Task<FileWriteResult> EditorInsertAtCursorAsync(string text, CancellationToken cancellationToken = default);

    // -------- Workspace event stream --------
    Task<WorkspaceEventsResult> WorkspaceEventsListAsync(int maxResults, CancellationToken cancellationToken = default);
    Task<WorkspaceEventsResult> WorkspaceWatchAsync(long sinceTimestampMs, int timeoutMs, int maxResults, CancellationToken cancellationToken = default);

    // -------- Tests --------
    Task<TestDiscoveryResult> TestDiscoverAsync(string? projectId, CancellationToken cancellationToken = default);
    Task<TestRunResult> TestRunAsync(string? filter, string? projectId, string? configuration, CancellationToken cancellationToken = default);

    // -------- NuGet --------
    Task<NuGetListResult> NugetListAsync(string? projectId, CancellationToken cancellationToken = default);
    Task<NuGetActionResult> NugetAddAsync(string projectId, string packageId, string? version, CancellationToken cancellationToken = default);
    Task<NuGetActionResult> NugetRemoveAsync(string projectId, string packageId, CancellationToken cancellationToken = default);

    // -------- Edit & Continue --------
    Task<EncStatusResult> DebugEncStatusAsync(CancellationToken cancellationToken = default);
    Task<EncApplyResult> DebugApplyCodeChangesAsync(CancellationToken cancellationToken = default);
    Task<EncApplyResult> DebugHotReloadAsync(CancellationToken cancellationToken = default);

    // -------- Code generation --------
    Task<AddMemberResult> CodeImplementInterfaceAsync(string file, string className, string interfaceName, CancellationToken cancellationToken = default);
    Task<AddMemberResult> CodeOverrideMemberAsync(string file, string className, string memberName, CancellationToken cancellationToken = default);
    Task<AddMemberResult> CodeGenerateConstructorAsync(string file, string className, IReadOnlyList<string>? fromFields, CancellationToken cancellationToken = default);
    Task<AddMemberResult> CodeGenerateEqualityAsync(string file, string className, CancellationToken cancellationToken = default);

    // -------- Context efficiency (issues #72-#89) --------

    // Phase 1
    Task<BuildSummaryResult> BuildSummaryAsync(string buildId, CancellationToken cancellationToken = default);
    Task<TestSummaryResult> TestRunSummaryAsync(string? filter, string? projectId, string? configuration, string mode, CancellationToken cancellationToken = default);
    Task<GroupedDiagnosticsResult> CodeDiagnosticsGroupedAsync(string? file, int maxResults, CancellationToken cancellationToken = default);
    Task<GroupedDiagnosticsResult> BuildErrorsGroupedAsync(string buildId, CancellationToken cancellationToken = default);

    // Phase 2
    Task<FileReadIfChangedResult> FileReadIfChangedAsync(string path, string? knownHash, FileRange? range, CancellationToken cancellationToken = default);
    Task<CodeDiffResult> CodeDiffAsync(string file, string? baseHash, CancellationToken cancellationToken = default);

    // Phase 3
    Task<FileOutlineResult> FileOutlineAsync(string file, CancellationToken cancellationToken = default);
    Task<FileInfoResult> FileInfoAsync(string file, CancellationToken cancellationToken = default);
    Task<SymbolSummaryResult> CodeSymbolSummaryAsync(string symbol, CancellationToken cancellationToken = default);
    Task<InvestigateResult> CodeInvestigateAsync(string symbol, int maxRefs, bool includeTests, CancellationToken cancellationToken = default);

    // Phase 4
    Task<SessionScopeResult> SessionScopeAsync(IReadOnlyList<string>? symbols, string? project, string? folder, CancellationToken cancellationToken = default);
    Task<SessionCurrentResult> SessionCurrentAsync(CancellationToken cancellationToken = default);
    Task SessionClearAsync(CancellationToken cancellationToken = default);
    Task<IoContextResult> IoContextAsync(CancellationToken cancellationToken = default);
    Task<GroupedDiagnosticsResult> CodeVerifyFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default);
    Task<TextSearchResult> SearchTextCompactAsync(string pattern, string? filePattern, string? projectId, IReadOnlyList<string>? kinds, int maxResults, string? cursor, CancellationToken cancellationToken = default);
    Task<DiagEventsResult> DiagEventsListInternedAsync(string? filter, int maxResults, CancellationToken cancellationToken = default);
    Task<VariableListResult> FrameLocalsSummaryAsync(int? threadId, int? frameIndex, CancellationToken cancellationToken = default);
}
