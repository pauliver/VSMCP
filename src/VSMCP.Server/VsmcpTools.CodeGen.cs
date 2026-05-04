using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "code.implement_interface")]
    [Description("Generate stubs for every interface member not yet implemented on the class. Each stub throws NotImplementedException. Idempotent: members already implemented are skipped. Inserts via Roslyn syntax tree (proper formatting + undo).")]
    public async Task<AddMemberResult> CodeImplementInterface(
        [Description("Absolute file path containing the class.")] string file,
        [Description("Class name (simple identifier).")] string className,
        [Description("Interface name (simple or fully qualified).")] string interfaceName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeImplementInterfaceAsync(file, className, interfaceName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.override_member")]
    [Description("Generate an override stub for a virtual or abstract member from a base class. Walks the base chain to find the first matching member.")]
    public async Task<AddMemberResult> CodeOverrideMember(
        [Description("Absolute file path containing the class.")] string file,
        [Description("Class name.")] string className,
        [Description("Member name to override (must be virtual/abstract on a base class).")] string memberName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeOverrideMemberAsync(file, className, memberName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.generate_constructor")]
    [Description("Generate a constructor that takes a parameter for each instance field and writable property and assigns them. Optionally restrict to a subset of field/property names.")]
    public async Task<AddMemberResult> CodeGenerateConstructor(
        [Description("Absolute file path.")] string file,
        [Description("Class name.")] string className,
        [Description("Field/property names to bind. Omit for all instance fields + writable properties.")] IReadOnlyList<string>? fromFields = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeGenerateConstructorAsync(file, className, fromFields, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.generate_equality")]
    [Description("Generate Equals(object) + GetHashCode() based on all instance fields and properties. Uses EqualityComparer<T>.Default for field comparison and HashCode.Combine for the hash.")]
    public async Task<AddMemberResult> CodeGenerateEquality(
        [Description("Absolute file path.")] string file,
        [Description("Class name.")] string className,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeGenerateEqualityAsync(file, className, ct).ConfigureAwait(false);
    }
}
