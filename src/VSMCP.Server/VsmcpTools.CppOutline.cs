using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.outline")]
    [Description("Topic-and-line outline for a C/C++ source or header file (.h/.hpp/.cpp/.cc/.cxx). Tokenizer-based — handles namespaces, classes, structs, unions, enums, typedefs, using-aliases, and function/method declarations. Not semantic, no template instantiation, no preprocessor evaluation; covers ~90% of typical declarations and is the right tool for an LLM that wants to navigate a header without file_read'ing the whole thing. For C# files use file_outline instead.")]
    public async Task<CppOutlineResult> CppOutline(
        [Description("Absolute path to the C/C++ source/header file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppOutlineAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.class_members")]
    [Description("List the members of a named class/struct in a C/C++ file. Wraps cpp_outline + filter. For inheritance walks or cross-file member discovery, use cpp_api_ref or grep.")]
    public async Task<CppMembersResult> CppClassMembers(
        [Description("Absolute path to the C/C++ source/header file.")] string file,
        [Description("Class or struct name to list members for.")] string className,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppClassMembersAsync(file, className, ct).ConfigureAwait(false);
    }
}
