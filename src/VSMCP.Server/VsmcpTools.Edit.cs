using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "edit.replace_all")]
    [Description("Replace all occurrences of a pattern in a single file. Pass regex=true for regex; otherwise literal string match. Returns the new file text and replacement count.")]
    public async Task<ReplaceAllResult> EditReplaceAll(
        [Description("Absolute file path.")] string file,
        [Description("Pattern (regex when regex=true; literal when false).")] string pattern,
        [Description("Replacement (regex back-refs $1 supported when regex=true).")] string replacement,
        [Description("Cap on replacements (omit for unlimited).")] int? maxReplacements = null,
        [Description("Treat pattern as regex. Default false (literal).")] bool regex = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        var (count, text) = await proxy.EditReplaceAllAsync(file, pattern, replacement, maxReplacements, regex, ct).ConfigureAwait(false);
        return new ReplaceAllResult { Replacements = count, Text = text };
    }

    [McpServerTool(Name = "edit.rename")]
    [Description("Solution-wide symbol rename via Roslyn Renamer. The rename is applied through Workspace.TryApplyChanges so it's grouped with VS undo and shows in open buffers. Pass dryRun=true to see all locations without applying.")]
    public async Task<RenameResult> EditRename(
        [Description("File containing the symbol's identifier.")] string file,
        [Description("Position (line/column) of the identifier.")] CodePosition position,
        [Description("New name.")] string newName,
        [Description("True to list locations only without applying. Default false.")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditRenameAsync(file, position, newName, dryRun, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.organize_usings")]
    [Description("Sort using directives (System first), optionally remove unused (CS8019), and optionally add missing usings inferred from CS0246/CS0103 diagnostics via code.suggest_usings. Applied via Roslyn workspace edit so undo is one operation.")]
    public async Task<OrganizeUsingsResult> EditOrganizeUsings(
        [Description("Absolute file path.")] string file,
        [Description("Add missing usings inferred from unresolved-symbol diagnostics. Default false.")] bool addMissing = false,
        [Description("Remove unused usings flagged by CS8019. Default true.")] bool removeUnused = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditOrganizeUsingsAsync(file, addMissing, removeUnused, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.insert_before")]
    [Description("Insert text immediately before the given line. Text is normalized to end with newline. Optionally open the file at the inserted location.")]
    public async Task<InsertResult> EditInsertBefore(
        [Description("Absolute file path.")] string file,
        [Description("1-based line number.")] int line,
        [Description("Text to insert.")] string text,
        [Description("Open the file at the insertion point. Default false.")] bool openInEditor = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditInsertBeforeAsync(file, line, text, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.insert_after")]
    [Description("Insert text immediately after the given line.")]
    public async Task<InsertResult> EditInsertAfter(
        [Description("Absolute file path.")] string file,
        [Description("1-based line number.")] int line,
        [Description("Text to insert.")] string text,
        [Description("Open the file at the insertion point. Default false.")] bool openInEditor = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditInsertAfterAsync(file, line, text, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.replace_member")]
    [Description("Replace a member declaration (method/property/field/event/ctor) with new text. The new text must parse as a single C# member. Trivia (leading whitespace/comments) is preserved from the original.")]
    public async Task<ReplaceMemberResult> EditReplaceMember(
        [Description("Absolute file path.")] string file,
        [Description("Containing type's simple name.")] string className,
        [Description("Member name to replace.")] string memberName,
        [Description("New member declaration source.")] string newText,
        [Description("Open the file at the replaced location. Default false.")] bool openInEditor = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditReplaceMemberAsync(file, className, memberName, newText, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.move_type")]
    [Description("Move a type declaration into its own file. Optionally specify a new namespace. Source usings are preserved on the new file. Returns Conflict=true if the target file already exists.")]
    public async Task<MoveTypeResult> EditMoveType(
        [Description("Absolute path to the source file containing the type.")] string file,
        [Description("Type name (simple identifier).")] string typeName,
        [Description("New namespace (defaults to source namespace).")] string? newNamespace = null,
        [Description("Target file path (default: <typeName>.cs in the source directory).")] string? newFile = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditMoveTypeAsync(file, typeName, newNamespace, newFile, ct).ConfigureAwait(false);
    }
}

public sealed class ReplaceAllResult
{
    public int Replacements { get; set; }
    public string Text { get; set; } = "";
}
