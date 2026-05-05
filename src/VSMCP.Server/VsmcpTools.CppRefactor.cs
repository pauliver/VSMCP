using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.find_references_solution")]
    [Description("Solution-wide find references for a C/C++ symbol. Walks every C/C++ TU in the solution looking for cursors whose canonical USR matches the symbol at (line, col). Slow first call (cold-parses each TU); warm calls hit the cached TUs. v2-of-cpp_find_references; the single-TU cpp_find_references is still useful for quick in-file checks.")]
    public async Task<CppLocationListResult> CppFindReferencesSolution(
        [Description("Absolute path to the seed C/C++ file.")] string file,
        [Description("1-based line of the symbol.")] int line,
        [Description("1-based column of the symbol.")] int column,
        [Description("Cap on the number of TUs walked (default 200). Larger = more coverage, slower first call.")] int maxFiles = 200,
        [Description("Extra include dirs.")] string[]? extraIncludes = null,
        [Description("Extra defines.")] string[]? extraDefines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppFindReferencesSolutionAsync(file, line, column, maxFiles, extraIncludes, extraDefines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.rename")]
    [Description("Symbol-aware C/C++ rename. Finds all references in the same translation unit via libclang, then text-replaces each occurrence with the new name. v1 is single-TU — solution-wide rename is a v2 add.")]
    public async Task<CppLocationListResult> CppRename(
        [Description("Absolute path to the C/C++ file.")] string file,
        [Description("1-based line of the symbol to rename.")] int line,
        [Description("1-based column of the symbol to rename.")] int column,
        [Description("New name. Must be a valid C++ identifier — no validation in v1.")] string newName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppRenameAsync(file, line, column, newName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.investigate")]
    [Description("Bundle of context for a named C/C++ symbol: declaration site + signature + libclang type info + brief comment + member body + cross-TU caller list. C++ analog of code_investigate. Composes cpp_find_symbol + cpp_quick_info + cpp_read_member + cpp_find_references_solution into one round trip.")]
    public async Task<CppInvestigateResult> CppInvestigate(
        [Description("Symbol name. First match wins.")] string symbol,
        [Description("Cap on the number of caller locations (default 50).")] int maxRefs = 50,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppInvestigateAsync(symbol, maxRefs, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.rename_solution")]
    [Description("Solution-wide symbol-aware C/C++ rename. Walks every C/C++ TU via libclang, finds references to the symbol at (line, col) by canonical USR, and rewrites each occurrence with newName. Slow first call (cold-parses each TU). v1: edits saved-disk content; call cpp_invalidate or rely on the auto-invalidate per file.")]
    public async Task<CppRenameSolutionResult> CppRenameSolution(
        [Description("Absolute path to the seed file containing the symbol.")] string file,
        [Description("1-based line of the symbol.")] int line,
        [Description("1-based column of the symbol.")] int column,
        [Description("New name.")] string newName,
        [Description("Cap on the number of TUs walked (default 200).")] int maxFiles = 200,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppRenameSolutionAsync(file, line, column, newName, maxFiles, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.move_type")]
    [Description("Move a C/C++ class/struct/union/enum declaration from sourceFile to targetFile. v1 is a header-only text move — if the type has out-of-line method definitions in a .cpp, those references stay where they are and may need manual #include adjustments. Creates the target file with #pragma once when missing (if createTargetIfMissing=true). C++ analog of edit_move_type.")]
    public async Task<CppMoveTypeResult> CppMoveType(
        [Description("Source file path.")] string sourceFile,
        [Description("Type name to move.")] string typeName,
        [Description("Target file path.")] string targetFile,
        [Description("Create target file if missing (default true).")] bool createTargetIfMissing = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppMoveTypeAsync(sourceFile, typeName, targetFile, createTargetIfMissing, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.move_method")]
    [Description("Move a C/C++ method body from sourceFile to targetFile. Auto-injects `ClassName::` qualification when the original method body lacks it (typical when moving an inline method to a .cpp). v1: text move; if the method touches private members the move may require a friend declaration. C++ analog of edit_move_method.")]
    public async Task<CppMoveMethodResult> CppMoveMethod(
        [Description("Source file path.")] string sourceFile,
        [Description("Containing class/struct name.")] string className,
        [Description("Method name to move.")] string methodName,
        [Description("Target file path.")] string targetFile,
        [Description("Create target file if missing (default true).")] bool createTargetIfMissing = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppMoveMethodAsync(sourceFile, className, methodName, targetFile, createTargetIfMissing, ct).ConfigureAwait(false);
    }
}
