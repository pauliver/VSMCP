using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// Generic batch dispatcher: runs <paramref name="run"/> per item, isolating per-item
    /// failures so one bad item doesn't fail the whole call. Preserves input ordering.
    /// </summary>
    private async Task<BatchResult<TOut>> RunBatchAsync<TIn, TOut>(
        IReadOnlyList<TIn>? items,
        Func<TIn, CancellationToken, Task<TOut>> run,
        CancellationToken ct)
    {
        if (items is null || items.Count == 0) return new BatchResult<TOut>();
        var batch = new BatchResult<TOut> { Total = items.Count };
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = new BatchItemResult<TOut> { Index = i };
            try
            {
                entry.Value = await run(items[i], ct).ConfigureAwait(false);
                entry.Success = true;
                batch.Succeeded++;
            }
            catch (VsmcpException ex)
            {
                entry.Error = new BatchItemError { Code = ex.Code, Message = ex.Message };
                batch.Failed++;
            }
            catch (Exception ex)
            {
                entry.Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message };
                batch.Failed++;
            }
            batch.Items.Add(entry);
        }
        return batch;
    }

    // ---- C++ batches ----

    public Task<BatchResult<CppReadMemberResult>> CppReadMemberManyAsync(IReadOnlyList<CppReadMemberItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppReadMemberAsync(i.File, i.ClassName, i.MemberName, c), ct);

    public Task<BatchResult<CppReplaceMemberResult>> CppReplaceMemberManyAsync(IReadOnlyList<CppReplaceMemberItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppReplaceMemberAsync(i.File, i.ClassName, i.MemberName, i.NewCode, c), ct);

    public Task<BatchResult<CppOrganizeIncludesResult>> CppOrganizeIncludesManyAsync(IReadOnlyList<string> files, CancellationToken ct = default)
        => RunBatchAsync(files, (f, c) => CppOrganizeIncludesAsync(f, c), ct);

    public Task<BatchResult<bool>> CppSetUnsavedBufferManyAsync(IReadOnlyList<CppUnsavedBufferItem> items, CancellationToken ct = default)
        => RunBatchAsync<CppUnsavedBufferItem, bool>(items, async (i, c) =>
        {
            await CppSetUnsavedBufferAsync(i.File, i.Content, c).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<BatchResult<bool>> CppInvalidateManyAsync(IReadOnlyList<string> files, CancellationToken ct = default)
        => RunBatchAsync<string, bool>(files, async (f, c) =>
        {
            await CppInvalidateAsync(f, c).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<BatchResult<CppLocationListResult>> CppRenameManyAsync(IReadOnlyList<CppRenameItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppRenameAsync(i.File, i.Line, i.Column, i.NewName, c), ct);

    public Task<BatchResult<CppMoveTypeResult>> CppMoveTypeManyAsync(IReadOnlyList<CppMoveTypeItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppMoveTypeAsync(i.SourceFile, i.TypeName, i.TargetFile, i.CreateTargetIfMissing, c), ct);

    public Task<BatchResult<CppMoveMethodResult>> CppMoveMethodManyAsync(IReadOnlyList<CppMoveMethodItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppMoveMethodAsync(i.SourceFile, i.ClassName, i.MethodName, i.TargetFile, i.CreateTargetIfMissing, c), ct);

    public Task<BatchResult<CppImplementInterfaceResult>> CppImplementInterfaceManyAsync(IReadOnlyList<CppImplementInterfaceItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppImplementInterfaceAsync(i.DerivedFile, i.DerivedClass, i.BaseClass, i.BaseClassFile, c), ct);

    public Task<BatchResult<CppGenerateCtorResult>> CppGenerateConstructorManyAsync(IReadOnlyList<CppGenerateCtorItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppGenerateConstructorAsync(i.File, i.ClassName, i.MemberNames, c), ct);

    public Task<BatchResult<CppOverrideMemberResult>> CppOverrideMemberManyAsync(IReadOnlyList<CppOverrideMemberItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppOverrideMemberAsync(i.File, i.ClassName, i.MethodName, i.ReturnType, i.Parameters, c), ct);

    public Task<BatchResult<CppGenerateEqualityResult>> CppGenerateEqualityManyAsync(IReadOnlyList<CppGenerateEqualityItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppGenerateEqualityAsync(i.File, i.ClassName, c), ct);

    public Task<BatchResult<CppQuickInfoResult>> CppQuickInfoManyAsync(IReadOnlyList<CodePosition> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppQuickInfoAsync(i.File, i.Line, i.Column, null, null, c), ct);

    public Task<BatchResult<CppLocationListResult>> CppFindReferencesManyAsync(IReadOnlyList<CodePosition> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppFindReferencesSemAsync(i.File, i.Line, i.Column, null, null, c), ct);

    public Task<BatchResult<CppLocationResult>> CppGotoDefinitionManyAsync(IReadOnlyList<CodePosition> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CppGotoDefinitionAsync(i.File, i.Line, i.Column, null, null, c), ct);

    // ---- C# / Roslyn batches ----

    public Task<BatchResult<ReplaceMemberResult>> EditReplaceMemberManyAsync(IReadOnlyList<EditReplaceMemberItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => EditReplaceMemberAsync(i.File, i.TypeName, i.MemberName, i.NewCode, openInEditor: false, c), ct);

    public Task<BatchResult<MoveTypeResult>> EditMoveTypeManyAsync(IReadOnlyList<EditMoveTypeItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => EditMoveTypeAsync(i.File, i.TypeName, i.NewNamespace, i.NewFile, i.AppendIfExists, c), ct);

    public Task<BatchResult<MoveTypeResult>> EditMoveMethodManyAsync(IReadOnlyList<EditMoveMethodItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => EditMoveMethodAsync(i.File, i.MethodName, i.ContainerType, i.NewFile, i.AppendIfExists, c), ct);

    public Task<BatchResult<RenameResult>> EditRenameManyAsync(IReadOnlyList<EditRenameItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) =>
            EditRenameAsync(i.File, new CodePosition { File = i.File, Line = i.Line, Column = i.Column }, i.NewName, dryRun: false, c), ct);

    public Task<BatchResult<AddUsingResult>> EditAddUsingManyAsync(IReadOnlyList<EditAddUsingItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => EditAddUsingAsync(i.File, i.Namespace, c), ct);

    public Task<BatchResult<AddIncludeResult>> EditAddIncludeManyAsync(IReadOnlyList<EditAddIncludeItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => EditAddIncludeAsync(i.File, i.IncludePath, i.System, c), ct);

    public Task<BatchResult<OrganizeUsingsResult>> EditOrganizeUsingsManyAsync(IReadOnlyList<string> files, CancellationToken ct = default)
        => RunBatchAsync(files, (f, c) => EditOrganizeUsingsAsync(f, addMissing: true, removeUnused: true, c), ct);

    public Task<BatchResult<AddMemberResult>> CodeImplementInterfaceManyAsync(IReadOnlyList<CodeImplementInterfaceItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CodeImplementInterfaceAsync(i.DerivedFile, i.DerivedClass, i.BaseClass, c), ct);

    public Task<BatchResult<AddMemberResult>> CodeOverrideMemberManyAsync(IReadOnlyList<CodeOverrideMemberItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CodeOverrideMemberAsync(i.File, i.DerivedClass, i.BaseMember, c), ct);

    public Task<BatchResult<AddMemberResult>> CodeGenerateConstructorManyAsync(IReadOnlyList<CodeGenerateCtorItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CodeGenerateConstructorAsync(i.File, i.ClassName, i.MemberNames, c), ct);

    public Task<BatchResult<AddMemberResult>> CodeGenerateEqualityManyAsync(IReadOnlyList<CodeGenerateEqualityItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CodeGenerateEqualityAsync(i.File, i.ClassName, c), ct);

    public Task<BatchResult<QuickInfoResult>> CodeQuickInfoManyAsync(IReadOnlyList<CodePosition> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => CodeQuickInfoAsync(i, c), ct);

    // ---- File batches ----

    public Task<BatchResult<MembersResult>> FileMembersManyAsync(IReadOnlyList<FileMembersItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => FileMembersAsync(i.File, i.ClassName, kinds: null, excludeInherited: false, c), ct);

    public Task<BatchResult<FileInfoResult>> FileInfoManyAsync(IReadOnlyList<string> files, CancellationToken ct = default)
        => RunBatchAsync(files, (f, c) => FileInfoAsync(f, c), ct);

    public Task<BatchResult<FileReadIfChangedResult>> FileReadIfChangedManyAsync(IReadOnlyList<FileReadIfChangedItem> items, CancellationToken ct = default)
        => RunBatchAsync(items, (i, c) => FileReadIfChangedAsync(i.File, knownHash: null, range: null, c), ct);

    public Task<BatchResult<FileOutlineResult>> FileOutlineManyAsync(IReadOnlyList<string> files, CancellationToken ct = default)
        => RunBatchAsync(files, (f, c) => FileOutlineAsync(f, c), ct);
}
