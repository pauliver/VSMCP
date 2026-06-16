using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// `_many` batch variants for class/method/edit tools (issue #122). Each wrapper takes a
/// list of per-item parameter bundles and returns a <see cref="BatchResult{T}"/> with
/// per-item success/error. Saves N MCP round-trips when an LLM is doing bursty refactor
/// work across many files or symbols.
/// </summary>
public sealed partial class VsmcpTools
{
    // ---- C++ batches ----

    [McpServerTool(Name = "cpp.read_member_many")]
    [Description("Read N member bodies in one call. items: [{file, className, memberName}].")]
    public async Task<BatchResult<CppReadMemberResult>> CppReadMemberMany(
        IReadOnlyList<CppReadMemberItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppReadMemberManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.replace_member_many")]
    [Description("Replace N member bodies in one call. items: [{file, className, memberName, newCode}].")]
    public async Task<BatchResult<CppReplaceMemberResult>> CppReplaceMemberMany(
        IReadOnlyList<CppReplaceMemberItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppReplaceMemberManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.organize_includes_many")]
    [Description("Tidy includes in N files in one call.")]
    public async Task<BatchResult<CppOrganizeIncludesResult>> CppOrganizeIncludesMany(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppOrganizeIncludesManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.set_unsaved_buffer_many")]
    [Description("Push or clear unsaved-buffer overrides for N files. items: [{file, content?}].")]
    public async Task<BatchResult<bool>> CppSetUnsavedBufferMany(
        IReadOnlyList<CppUnsavedBufferItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppSetUnsavedBufferManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.invalidate_many")]
    [Description("Drop cached translation units for N files in one call.")]
    public async Task<BatchResult<bool>> CppInvalidateMany(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppInvalidateManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.rename_many")]
    [Description("Rename N symbols at given (file, line, column) positions.")]
    public async Task<BatchResult<CppLocationListResult>> CppRenameMany(
        IReadOnlyList<CppRenameItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppRenameManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.move_type_many")]
    [Description("Move N C++ types between files. items: [{sourceFile, typeName, targetFile, createTargetIfMissing}].")]
    public async Task<BatchResult<CppMoveTypeResult>> CppMoveTypeMany(
        IReadOnlyList<CppMoveTypeItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppMoveTypeManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.move_method_many")]
    [Description("Move N C++ methods between files.")]
    public async Task<BatchResult<CppMoveMethodResult>> CppMoveMethodMany(
        IReadOnlyList<CppMoveMethodItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppMoveMethodManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.implement_interface_many")]
    [Description("Apply N implement-interface plans in one call.")]
    public async Task<BatchResult<CppImplementInterfaceResult>> CppImplementInterfaceMany(
        IReadOnlyList<CppImplementInterfaceItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppImplementInterfaceManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.generate_constructor_many")]
    [Description("Generate constructors for N C++ classes/structs in one call.")]
    public async Task<BatchResult<CppGenerateCtorResult>> CppGenerateConstructorMany(
        IReadOnlyList<CppGenerateCtorItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppGenerateConstructorManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.override_member_many")]
    [Description("Generate override stubs for N (className, methodName) pairs.")]
    public async Task<BatchResult<CppOverrideMemberResult>> CppOverrideMemberMany(
        IReadOnlyList<CppOverrideMemberItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppOverrideMemberManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.generate_equality_many")]
    [Description("Generate operator== for N C++ classes/structs.")]
    public async Task<BatchResult<CppGenerateEqualityResult>> CppGenerateEqualityMany(
        IReadOnlyList<CppGenerateEqualityItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppGenerateEqualityManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.quick_info_many")]
    [Description("Quick-info (libclang) for N (file, line, column) positions.")]
    public async Task<BatchResult<CppQuickInfoResult>> CppQuickInfoMany(
        IReadOnlyList<CodePosition> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppQuickInfoManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.find_references_many")]
    [Description("Single-TU find-references for N positions. Use cpp.find_references_solution for cross-TU.")]
    public async Task<BatchResult<CppLocationListResult>> CppFindReferencesMany(
        IReadOnlyList<CodePosition> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppFindReferencesManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.goto_definition_many")]
    [Description("Goto-definition for N positions (libclang).")]
    public async Task<BatchResult<CppLocationResult>> CppGotoDefinitionMany(
        IReadOnlyList<CodePosition> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CppGotoDefinitionManyAsync(items, ct).ConfigureAwait(false);
    }

    // ---- C# / Roslyn batches ----

    [McpServerTool(Name = "edit.replace_member_many")]
    [Description("Replace N C# member bodies in one call.")]
    public async Task<BatchResult<ReplaceMemberResult>> EditReplaceMemberMany(
        IReadOnlyList<EditReplaceMemberItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditReplaceMemberManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.move_type_many")]
    [Description("Move N C# types between files in one call.")]
    public async Task<BatchResult<MoveTypeResult>> EditMoveTypeMany(
        IReadOnlyList<EditMoveTypeItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditMoveTypeManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.move_method_many")]
    [Description("Move N C# methods between files in one call.")]
    public async Task<BatchResult<MoveTypeResult>> EditMoveMethodMany(
        IReadOnlyList<EditMoveMethodItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditMoveMethodManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.rename_many")]
    [Description("Apply N semantic renames (Roslyn) in one call.")]
    public async Task<BatchResult<RenameResult>> EditRenameMany(
        IReadOnlyList<EditRenameItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditRenameManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.add_using_many")]
    [Description("Add N usings across N files. items: [{file, namespace}].")]
    public async Task<BatchResult<AddUsingResult>> EditAddUsingMany(
        IReadOnlyList<EditAddUsingItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditAddUsingManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.add_include_many")]
    [Description("Add N #includes across N C++ files. items: [{file, includePath, system}].")]
    public async Task<BatchResult<AddIncludeResult>> EditAddIncludeMany(
        IReadOnlyList<EditAddIncludeItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditAddIncludeManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.organize_usings_many")]
    [Description("Organize usings across N C# files in one call.")]
    public async Task<BatchResult<OrganizeUsingsResult>> EditOrganizeUsingsMany(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.EditOrganizeUsingsManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.implement_interface_many")]
    [Description("Apply N C# implement-interface plans in one call.")]
    public async Task<BatchResult<AddMemberResult>> CodeImplementInterfaceMany(
        IReadOnlyList<CodeImplementInterfaceItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CodeImplementInterfaceManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.override_member_many")]
    [Description("Generate override stubs for N C# (derivedClass, baseMember) pairs.")]
    public async Task<BatchResult<AddMemberResult>> CodeOverrideMemberMany(
        IReadOnlyList<CodeOverrideMemberItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CodeOverrideMemberManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.generate_constructor_many")]
    [Description("Generate constructors for N C# classes in one call.")]
    public async Task<BatchResult<AddMemberResult>> CodeGenerateConstructorMany(
        IReadOnlyList<CodeGenerateCtorItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CodeGenerateConstructorManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.generate_equality_many")]
    [Description("Generate Equals/GetHashCode for N C# classes in one call.")]
    public async Task<BatchResult<AddMemberResult>> CodeGenerateEqualityMany(
        IReadOnlyList<CodeGenerateEqualityItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CodeGenerateEqualityManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.quick_info_many")]
    [Description("Roslyn quick-info for N (file, line, column) positions.")]
    public async Task<BatchResult<QuickInfoResult>> CodeQuickInfoMany(
        IReadOnlyList<CodePosition> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.CodeQuickInfoManyAsync(items, ct).ConfigureAwait(false);
    }

    // ---- File batches ----

    [McpServerTool(Name = "file.members_many")]
    [Description("List members of N classes across N files in one call. items: [{file, className}].")]
    public async Task<BatchResult<MembersResult>> FileMembersMany(
        IReadOnlyList<FileMembersItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.FileMembersManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.info_many")]
    [Description("Get file info (size, language, hash, etc.) for N files in one call.")]
    public async Task<BatchResult<FileInfoResult>> FileInfoMany(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.FileInfoManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.read_if_changed_many")]
    [Description("Conditional read for N files. items: [{file, sinceTimestampMs}].")]
    public async Task<BatchResult<FileReadIfChangedResult>> FileReadIfChangedMany(
        IReadOnlyList<FileReadIfChangedItem> items, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.FileReadIfChangedManyAsync(items, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.outline_many")]
    [Description("Outline N files (any language) in one call.")]
    public async Task<BatchResult<FileOutlineResult>> FileOutlineMany(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var p = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await p.FileOutlineManyAsync(files, ct).ConfigureAwait(false);
    }
}
