using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.analyzer_status")]
    [Description("Health check for the out-of-process VSMCP.CppAnalyzer sidecar. Returns whether it's been spawned, the process state (alive / exited with code), the log file path under %LOCALAPPDATA%\\VSMCP\\logs\\, and the last N captured stderr/stdout lines. Use after a cpp_* call fails to see what libclang said.")]
    public async Task<CppAnalyzerStatusResult> CppAnalyzerStatus(
        [Description("Number of recent log lines to include (default 50, cap 200).")] int recentLogLines = 50,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppAnalyzerStatusAsync(recentLogLines, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.read_member")]
    [Description("Read a single C/C++ class/struct member's body by name. Saves tokens vs file_read of the whole file. Uses cpp_outline to locate the line, then walks braces to find the closing '}'. C++ analog of code_read_member.")]
    public async Task<CppReadMemberResult> CppReadMember(
        [Description("Absolute path to the C/C++ file.")] string file,
        [Description("Class or struct name containing the member.")] string className,
        [Description("Member name (method, field, etc).")] string memberName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppReadMemberAsync(file, className, memberName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.organize_includes")]
    [Description("Sort, dedupe, and group #includes at the top of a C/C++ file. System includes (<>) come first sorted alphabetically; local includes (\"\") come second. C++ analog of edit_organize_usings.")]
    public async Task<CppOrganizeIncludesResult> CppOrganizeIncludes(
        [Description("Absolute path to the C/C++ file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppOrganizeIncludesAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.symbol_summary")]
    [Description("Compact symbol summary for a C/C++ name across the solution: each match's location, type, and brief comment. Aggregates cpp_find_symbol + cpp_quick_info. C++ analog of code_symbol_summary.")]
    public async Task<CppSymbolSummaryResult> CppSymbolSummary(
        [Description("Symbol name (exact, or glob).")] string symbol,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppSymbolSummaryAsync(symbol, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.replace_member")]
    [Description("Rewrite the body of a C/C++ class/struct method by name. Locates the member via cpp_outline, walks braces to find its closing '}', and replaces the whole declaration with newCode. C++ analog of edit_replace_member.")]
    public async Task<CppReplaceMemberResult> CppReplaceMember(
        [Description("Absolute path to the C/C++ file.")] string file,
        [Description("Class or struct name.")] string className,
        [Description("Member name to replace.")] string memberName,
        [Description("Replacement source. Should include the full method (signature + body).")] string newCode,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppReplaceMemberAsync(file, className, memberName, newCode, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.generate_constructor")]
    [Description("Generate a C/C++ constructor that initializes a class's member fields. Walks the class body via cpp_outline, picks out simple field declarations, and inserts a `Foo(T1 a_, T2 b_) : a(a_), b(b_) { }` style ctor before the closing brace. C++ analog of code_generate_constructor.")]
    public async Task<CppGenerateCtorResult> CppGenerateConstructor(
        [Description("Absolute path to the C/C++ file.")] string file,
        [Description("Class or struct name to generate the constructor on.")] string className,
        [Description("Optional list of field names to include. Omit to use all detected fields.")] IReadOnlyList<string>? memberNames = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppGenerateConstructorAsync(file, className, memberNames, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.override_member")]
    [Description("Insert an `override` stub for a virtual method into a C/C++ class. Caller supplies returnType + parameters since v1 does not introspect the base class via libclang. C++ analog of code_override_member.")]
    public async Task<CppOverrideMemberResult> CppOverrideMember(
        [Description("Absolute path to the C/C++ file containing the derived class.")] string file,
        [Description("Derived class name.")] string className,
        [Description("Method name to override.")] string methodName,
        [Description("Return type as it appears in the base class (e.g. 'void', 'int', 'std::string').")] string returnType,
        [Description("Parameter list (between the parens, e.g. 'int x, const char* msg'). Empty allowed.")] string parameters,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppOverrideMemberAsync(file, className, methodName, returnType, parameters, ct).ConfigureAwait(false);
    }
}
