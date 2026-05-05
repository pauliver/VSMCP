using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "cpp.outline_many")]
    [Description("Batch cpp_outline. Returns the structured outline for each file in one round trip; per-file errors are reported in the entry's Error field instead of aborting the whole call. C++ analog of code_symbols_many.")]
    public async Task<CppOutlineManyResult> CppOutlineMany(
        [Description("Absolute paths to C/C++ files.")] IReadOnlyList<string> files,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppOutlineManyAsync(files, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.inheritance")]
    [Description("Walk the inheritance hierarchy of a C/C++ class. Returns a tree of base classes (recursively, up to maxDepth). Each base node carries the access keyword (public/private/protected) as declared. Resolves base-class declarations via cpp_find_symbol across the solution. C++ analog of file_inheritance.")]
    public async Task<CppInheritanceResult> CppInheritance(
        [Description("Path to the file containing the derived class.")] string file,
        [Description("Class or struct name to start from.")] string className,
        [Description("Recursion depth (default 4).")] int maxDepth = 4,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppInheritanceAsync(file, className, maxDepth, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.generate_equality")]
    [Description("Generate operator== and operator!= for a C/C++ class, comparing all detected member fields. Inserts before the closing brace. C++ analog of code_generate_equality.")]
    public async Task<CppGenerateEqualityResult> CppGenerateEquality(
        [Description("Path to the file containing the class.")] string file,
        [Description("Class or struct name.")] string className,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppGenerateEqualityAsync(file, className, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.implement_interface")]
    [Description("Generate override stubs for every virtual method declared in the base class, inserted into the derived class. Skips methods that derived already overrides. baseClassFile is auto-discovered via cpp_find_symbol if omitted. C++ analog of code_implement_interface.")]
    public async Task<CppImplementInterfaceResult> CppImplementInterface(
        [Description("Path to the file containing the derived class.")] string derivedFile,
        [Description("Derived class/struct name.")] string derivedClass,
        [Description("Base class/struct name to implement.")] string baseClass,
        [Description("Optional path to the file containing the base class (auto-discovered if omitted).")] string? baseClassFile = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppImplementInterfaceAsync(derivedFile, derivedClass, baseClass, baseClassFile, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.scaffold_file")]
    [Description("Create a new .h header (and optionally a matching .cpp) with a class skeleton. The header gets `#pragma once`, an optional namespace block, and a class with an empty constructor + destructor. The cpp file gets the matching out-of-line definitions. C++ analog of code_scaffold_file.")]
    public async Task<CppScaffoldFileResult> CppScaffoldFile(
        [Description("Absolute path for the new .h/.hpp header. Must not exist.")] string headerPath,
        [Description("Class name to scaffold.")] string className,
        [Description("Optional namespace to wrap the declaration in.")] string? @namespace = null,
        [Description("When true (default), also create a matching .cpp file alongside the header.")] bool createCpp = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppScaffoldFileAsync(headerPath, className, @namespace, createCpp, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "cpp.suggest_includes")]
    [Description("Given a symbol name (e.g. 'std::vector', 'Bus', 'std::string'), find headers across the solution that declare it. Headers (.h/.hpp/.hxx) are ranked above .cpp/.cc. C++ analog of code_suggest_usings.")]
    public async Task<CppSuggestIncludesResult> CppSuggestIncludes(
        [Description("Symbol name. Exact, or glob with * and ?.")] string symbol,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CppSuggestIncludesAsync(symbol, ct).ConfigureAwait(false);
    }
}
