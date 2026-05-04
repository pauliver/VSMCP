using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.header_lookup")]
    [Description("Find a C/C++ symbol's declaration by walking the #include chain of the given file. Best-effort: matches function declarations, class/struct/enum, typedef, and using-aliases via regex. No semantic analysis.")]
    public async Task<HeaderLookupResult> CppHeaderLookup(
        [Description("C/C++ source file (the search starts from its #includes).")] string file,
        [Description("Symbol name to look up.")] string symbolName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppHeaderLookupAsync(file, symbolName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.include_chain")]
    [Description("Recursively walk all #includes from a file. Returns each header with its source line and whether it was a system or local include. Cycles are broken.")]
    public async Task<IncludeChainResult> CppIncludeChain(
        [Description("C/C++ source or header file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppIncludeChainAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.macro_lookup")]
    [Description("Find a #define and its usages across all C/C++ files in the solution. Returns the first definition found (location + expansion text) and a list of files referencing the name.")]
    public async Task<MacroResult> CppMacroLookup(
        [Description("Macro name (e.g. 'MY_MACRO').")] string name,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppMacroLookupAsync(name, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.preprocess")]
    [Description("Run the C/C++ preprocessor on a file. Currently returns Unsupported — full preprocessing requires invoking cl.exe with /P or /E. Use VS's 'Preprocess File' command from a Developer Prompt.")]
    public async Task<PreprocessResult> CppPreprocess(
        [Description("C/C++ source file.")] string file,
        [Description("Additional defines (e.g. 'DEBUG', 'FOO=1').")] IReadOnlyList<string>? defines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppPreprocessAsync(file, defines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.api_ref")]
    [Description("Look up an API in an offline reference DB. Currently returns Unsupported — no DB ships with VSMCP. Use code.quick_info on a usage or search docs.microsoft.com.")]
    public async Task<ApiReferenceResult> CppApiRef(
        [Description("API name (e.g. 'CreateFile').")] string apiName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppApiRefAsync(apiName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.generated_file")]
    [Description("Inspect generator → output mapping for build-event-generated files (MIDL, etc). Currently returns Unsupported — requires per-project build-system integration.")]
    public async Task<GeneratedFileInfo> CppGeneratedFile(
        [Description("File path.")] string file,
        [Description("Generator type (e.g. 'midl', 'tlb').")] string type,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppGeneratedFileAsync(file, type, ct).ConfigureAwait(false);
    }
}
