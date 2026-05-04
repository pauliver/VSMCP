using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "code.find_symbol")]
    [Description("Find symbols by name (or qualified name like 'ClassName.MemberName') across the entire solution. Returns location + signature so subsequent edits can target the symbol without reading whole files. Cornerstone of semantic editing — replaces 3-step file-read+grep+lineno discovery with a single lookup.")]
    public async Task<SymbolMatchResult> CodeFindSymbol(
        [Description("Symbol name. Use 'Class.Member' for qualified lookup.")] string name,
        [Description("Symbol kind filter: 'method', 'property', 'field', 'event', 'namedtype', 'namespace'. Omit for all.")] string? kind = null,
        [Description("Max results (default 100).")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeFindSymbolAsync(name, kind, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.read_member")]
    [Description("Return only the source of a single member (method/property/field/event/ctor) given its containing class. Saves ~90% of tokens vs reading the whole file. Pass file=null to auto-locate via code.find_symbol.")]
    public async Task<ReadMemberResult> CodeReadMember(
        [Description("Absolute file path. Omit to auto-locate.")] string? file,
        [Description("Containing class simple name.")] string className,
        [Description("Member name.")] string memberName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeReadMemberAsync(file, className, memberName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.add_member")]
    [Description("Add a new member to an existing class. The new member is parsed and inserted before the closing brace (or before the named insertBefore member). The edit is grouped with VS undo via the Roslyn workspace.")]
    public async Task<AddMemberResult> EditAddMember(
        [Description("Absolute file path. Omit to auto-locate via code.find_symbol(className).")] string? file,
        [Description("Containing class name.")] string className,
        [Description("Full member declaration source (must parse as a single C# member).")] string memberCode,
        [Description("Insert before this existing member's first line. Omit to append at class end.")] string? insertBefore = null,
        [Description("Open the file at the insertion line. Default false.")] bool openInEditor = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditAddMemberAsync(file, className, memberCode, insertBefore, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.add_using")]
    [Description("Add a 'using NS;' directive to a C# file. Idempotent: returns AlreadyPresent=true if the directive exists. Re-sorts using directives (System namespaces first).")]
    public async Task<AddUsingResult> EditAddUsing(
        [Description("Absolute C# file path.")] string file,
        [Description("Namespace to add (e.g. 'System.Linq').")] string namespaceName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditAddUsingAsync(file, namespaceName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.remove_using")]
    [Description("Remove a using directive by namespace name. Returns WasPresent=false if the directive wasn't there.")]
    public async Task<RemoveUsingResult> EditRemoveUsing(
        [Description("Absolute C# file path.")] string file,
        [Description("Namespace to remove.")] string namespaceName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditRemoveUsingAsync(file, namespaceName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.suggest_usings")]
    [Description("Suggest namespaces for unresolved symbol names in a file. Reads CS0246/CS0103 diagnostics, looks up matching public types via Roslyn SymbolFinder, returns namespace + confidence score. Use this before edit.add_using for 'fix unresolved name' workflows.")]
    public async Task<UsingSuggestionsResult> CodeSuggestUsings(
        [Description("Absolute C# file path.")] string file,
        [Description("Optional explicit names to look up. Omit to scan diagnostics.")] IReadOnlyList<string>? symbolNames = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeSuggestUsingsAsync(file, symbolNames, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "edit.add_include")]
    [Description("Add a '#include' directive to a C/C++ file. Inserts after the last existing #include (or after #pragma once / leading comments). Idempotent.")]
    public async Task<AddIncludeResult> EditAddInclude(
        [Description("Absolute C/C++ file path.")] string file,
        [Description("Header path text as it should appear in the directive (e.g. 'foo.h').")] string headerPath,
        [Description("True for system include (<>), false for user include (\"\"). Default false.")] bool isSystem = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditAddIncludeAsync(file, headerPath, isSystem, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.namespace_for_path")]
    [Description("Compute the proper C# namespace for a given relative path within a project. Reads the project's RootNamespace and appends sanitized folder segments. Returns the suggested absolute file path too.")]
    public async Task<NamespaceInfo> ProjectNamespaceForPath(
        [Description("Project unique-name or display name.")] string projectId,
        [Description("Relative folder path within the project (e.g. 'src/Services').")] string relativePath,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectNamespaceForPathAsync(projectId, relativePath, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.scaffold_file")]
    [Description("Create a new file in the project: infers namespace via project.namespace_for_path, generates language-appropriate boilerplate, writes to disk, and adds the file to the project. content overrides the generated boilerplate.")]
    public async Task<ScaffoldResult> CodeScaffoldFile(
        [Description("Project unique-name.")] string projectId,
        [Description("Relative file path (e.g. 'src/Services/Foo.cs').")] string relativePath,
        [Description("File contents. Omit for default boilerplate based on the file extension.")] string? content = null,
        [Description("Language hint: 'csharp', 'cpp', 'visualbasic'. Inferred from extension if omitted.")] string? language = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeScaffoldFileAsync(projectId, relativePath, content, language, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.create_class")]
    [Description("Create a new C# class file with proper namespace, optional base class and interfaces, and add it to a project. One call replaces the 5+ that would otherwise be needed (find namespace, scaffold file, add to project, etc).")]
    public async Task<CreateClassResult> CodeCreateClass(
        [Description("Class name.")] string name,
        [Description("Base class name (simple or qualified).")] string? baseClass = null,
        [Description("Interfaces to implement.")] IReadOnlyList<string>? interfaces = null,
        [Description("Project unique-name. Omit to use the first project in the solution.")] string? projectId = null,
        [Description("Folder under the project root (e.g. 'Services'). Default: project root.")] string? folder = null,
        [Description("Generate stubs for abstract base members. Default true.")] bool generateStubs = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeCreateClassAsync(name, baseClass, interfaces, projectId, folder, generateStubs, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.create_class")]
    [Description("Create a C++ header + source file pair (.h and .cpp). Optionally inherits a base class (adds #include). Both files are added to the project.")]
    public async Task<CppCreateClassResult> CppCreateClass(
        [Description("Class name.")] string name,
        [Description("Base class name (its header is #included).")] string? baseClass = null,
        [Description("Folder for the .h file. Default 'include'.")] string? headerFolder = null,
        [Description("Folder for the .cpp file. Default 'src'.")] string? sourceFolder = null,
        [Description("Project unique-name. Omit for the first project.")] string? projectId = null,
        [Description("Generate virtual member stubs from base class. Default true.")] bool generateVirtualStubs = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppCreateClassAsync(name, baseClass, headerFolder, sourceFolder, projectId, generateVirtualStubs, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.open_at_symbol")]
    [Description("Find a symbol by name and open the editor at its declaration. Combines code.find_symbol + editor.open in one call. Use 'Class.Member' for qualified names.")]
    public async Task<NavigateResult> EditorOpenAtSymbol(
        [Description("Symbol name (qualified or simple).")] string symbolPath,
        [Description("Symbol kind filter to disambiguate.")] string? kind = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorOpenAtSymbolAsync(symbolPath, kind, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.set_on_entry")]
    [Description("Set a line breakpoint at the first line of a method by name. Combines code.find_symbol + bp.set in one call.")]
    public async Task<BreakpointInfo> BreakpointSetOnEntry(
        [Description("Containing class simple name.")] string className,
        [Description("Method name.")] string methodName,
        [Description("Optional condition expression.")] string? condition = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointSetOnEntryAsync(className, methodName, condition, ct).ConfigureAwait(false);
    }
}
