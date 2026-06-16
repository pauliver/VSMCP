using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "project.replace_member")]
    [Description("Replace a class member across the entire project without needing to specify the file path. Automatically locates the class by name, and delegates to edit.replace_member or cpp.replace_member based on the file language.")]
    public async Task<ReplaceMemberResult> ProjectReplaceMember(
        [Description("Class or struct name containing the member.")] string className,
        [Description("Member name to replace.")] string memberName,
        [Description("Replacement source. Must parse as a single member.")] string newCode,
        [Description("Language hint: 'csharp' or 'cpp'. Omit to auto-detect.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectReplaceMemberAsync(className, memberName, newCode, language, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.add_member")]
    [Description("Add a new member to an existing class across the entire project. Automatically locates the class by name.")]
    public async Task<AddMemberResult> ProjectAddMember(
        [Description("Class or struct name to add the member to.")] string className,
        [Description("Member declaration source. Must parse as a single member.")] string newCode,
        [Description("Insert before this existing member's first line. Omit to append at class end.")] string? insertBefore = null,
        [Description("Language hint: 'csharp' or 'cpp'. Omit to auto-detect.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectAddMemberAsync(className, newCode, insertBefore, language, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.add_member_to_subclasses")]
    [Description("Find all classes that inherit from a specific base class or implement an interface, and add a member to all of them. Performs bulk modification.")]
    public async Task<BatchResult<AddMemberResult>> ProjectAddMemberToSubclasses(
        [Description("Base type name or interface to find derived classes for.")] string baseType,
        [Description("Member declaration source to add to all subclasses.")] string newCode,
        [Description("Language hint: 'csharp' or 'cpp'. Omit to auto-detect.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectAddMemberToSubclassesAsync(baseType, newCode, language, ct).ConfigureAwait(false);
    }


    [McpServerTool(Name = "project.investigate_symbol")]
    [Description("Get a complete semantic map of a symbol across the project. Includes base types, subclasses, members, and a usage count.")]
    public async Task<InvestigateSymbolResult> ProjectInvestigateSymbol(
        [Description("Symbol name to investigate.")] string symbol,
        [Description("Language hint: 'csharp' or 'cpp'. Omit to auto-detect.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectInvestigateSymbolAsync(symbol, language, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.call_graph")]
    [Description("Trace callers and callees of a specific method. Returns a tree graph up to a specified depth.")]
    public async Task<CallGraphResult> ProjectCallGraph(
        [Description("Class name containing the method.")] string className,
        [Description("Method name to trace.")] string methodName,
        [Description("Maximum depth to trace calls. Default 1.")] int maxDepth = 1,
        [Description("Language hint: 'csharp' or 'cpp'. Omit to auto-detect.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectCallGraphAsync(className, methodName, maxDepth, language, ct).ConfigureAwait(false);
    }

}
