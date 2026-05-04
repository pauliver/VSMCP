using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "file.list")]
    [Description("List files / folders / projects in the current solution. Optionally scope by project, by relative folder, by glob name pattern, or by item kind ('file', 'folder', 'project'). Use this before file.read to discover what exists, especially in large solutions.")]
    public async Task<FileListResult> FileList(
        [Description("Project unique-name (or display name). Omit to search the whole solution.")] string? projectId = null,
        [Description("Relative folder under the project root (forward or backslashes). Omit for whole project.")] string? folder = null,
        [Description("Glob pattern: '*', '?', '**', '{a,b}'. Matched against name and path. Omit for all.")] string? pattern = null,
        [Description("Item kinds: 'file', 'folder', 'project'. Omit for all.")] IReadOnlyList<string>? kinds = null,
        [Description("Max results (default 1000, cap 50000).")] int maxResults = 1000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileListAsync(projectId, folder, pattern, kinds, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.glob")]
    [Description("Match multiple glob patterns across the solution and return matching files. Patterns are OR-combined; each supports '*', '?', '**', '{a,b}'. Use this when you want files by extension or naming convention.")]
    public async Task<FileListResult> FileGlob(
        [Description("Glob patterns. A file matching any pattern is included.")] IReadOnlyList<string> patterns,
        [Description("Project unique-name to scope. Omit for all projects.")] string? projectId = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileGlobAsync(patterns, projectId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.classes")]
    [Description("Return all top-level types and namespaces declared across the solution (Roslyn). Filter by project, by namespace container, or by symbol kind. Use this instead of file.read to discover where types are without pulling whole files.")]
    public async Task<ClassesResult> FileClasses(
        [Description("Project name to scope (matches Project.Name or AssemblyName).")] string? projectId = null,
        [Description("Namespace container to filter by (e.g. 'MyApp.Models'). Exact match.")] string? @namespace = null,
        [Description("Symbol kinds: 'namedtype', 'namespace'. Omit for both.")] IReadOnlyList<string>? kinds = null,
        [Description("Max results (default 1000).")] int maxResults = 1000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileClassesAsync(projectId, @namespace, kinds, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.members")]
    [Description("List members (methods, properties, fields, events) of a class declared in a file. By default returns only declared members; pass excludeInherited=false to walk the base-class chain (excluding System.Object).")]
    public async Task<MembersResult> FileMembers(
        [Description("Absolute path to the file containing the type.")] string file,
        [Description("Class name (simple identifier, not fully-qualified).")] string className,
        [Description("Member kinds: 'method', 'property', 'field', 'event', 'namedtype'. Omit for all.")] IReadOnlyList<string>? kinds = null,
        [Description("True (default) to return only directly declared members. False to include inherited.")] bool excludeInherited = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileMembersAsync(file, className, kinds, excludeInherited, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.inheritance")]
    [Description("Return the inheritance graph for a class: base type chain (nearest first), all implemented interfaces, all derived/implementing types in the solution, and a flat hierarchy path.")]
    public async Task<InheritanceResult> FileInheritance(
        [Description("Absolute path to the file containing the type.")] string file,
        [Description("Class name (simple identifier).")] string className,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileInheritanceAsync(file, className, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.dependencies")]
    [Description("Parse '#include' directives from a C/C++ source or header file. Returns each include's text, line, and whether it's a system or local include. No semantic resolution.")]
    public async Task<DependencyListResult> FileDependencies(
        [Description("Absolute path to a C/C++ source or header file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileDependenciesAsync(file, ct).ConfigureAwait(false);
    }
}
