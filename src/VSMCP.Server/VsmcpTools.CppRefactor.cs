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
}
