using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<ReplaceMemberResult> ProjectReplaceMemberAsync(string className, string memberName, string newCode, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(className, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Classes.Count > 0 && searchRes.Classes[0].Location != null)
        {
            var file = searchRes.Classes[0].Location!.File;
            var lang = SourceLanguageDetector.FromPath(file);
            var isCsharp = lang == SourceLanguage.CSharp;
            var isCpp = lang == SourceLanguage.Cpp;

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                return await EditReplaceMemberAsync(file, className, memberName, newCode, false, cancellationToken).ConfigureAwait(false);
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                var res = await CppReplaceMemberAsync(file, className, memberName, newCode, cancellationToken).ConfigureAwait(false);
                return new ReplaceMemberResult { Replaced = res.Replaced, Line = res.StartLine };
            }

            throw new VsmcpException(ErrorCodes.NotFound, $"Could not auto-detect language for file {file}");
        }

        throw new VsmcpException(ErrorCodes.NotFound, $"Could not find class {className}");
    }

    public async Task<AddMemberResult> ProjectAddMemberAsync(string className, string newCode, string? insertBefore, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(className, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Classes.Count > 0 && searchRes.Classes[0].Location != null)
        {
            var file = searchRes.Classes[0].Location!.File;
            var lang = SourceLanguageDetector.FromPath(file);
            var isCsharp = lang == SourceLanguage.CSharp;
            var isCpp = lang == SourceLanguage.Cpp;

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                return await EditAddMemberAsync(file, className, newCode, insertBefore, false, cancellationToken).ConfigureAwait(false);
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                throw new VsmcpException(ErrorCodes.Unsupported, $"project.add_member is currently not supported for C++ due to lack of cpp_add_member");
            }

            throw new VsmcpException(ErrorCodes.NotFound, $"Could not auto-detect language for file {file}");
        }

        throw new VsmcpException(ErrorCodes.NotFound, $"Could not find class {className} for adding member");
    }

    public async Task<BatchResult<AddMemberResult>> ProjectAddMemberToSubclassesAsync(string baseType, string newCode, string? language, CancellationToken cancellationToken = default)
    {
        // Find all subclasses / implementations
        var searchRes = await SearchClassesAsync(null, baseType, baseType, 500, cancellationToken).ConfigureAwait(false);

        var batch = new BatchResult<AddMemberResult>();
        int index = 0;
        foreach (var match in searchRes.Classes)
        {
            if (match.Location == null) continue;
            var className = match.Name;
            var file = match.Location.File;

            var lang = SourceLanguageDetector.FromPath(file);
            var isCsharp = lang == SourceLanguage.CSharp;
            var isCpp = lang == SourceLanguage.Cpp;

            if (isCsharp)
            {
                try
                {
                    var res = await EditAddMemberAsync(file, className, newCode, null, false, cancellationToken).ConfigureAwait(false);
                    batch.Items.Add(new BatchItemResult<AddMemberResult> { Index = index, Success = true, Value = res });
                    batch.Succeeded++;
                }
                catch (Exception ex)
                {
                    batch.Items.Add(new BatchItemResult<AddMemberResult> { Index = index, Success = false, Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message } });
                    batch.Failed++;
                }
            }
            else if (isCpp)
            {
                batch.Items.Add(new BatchItemResult<AddMemberResult> { Index = index, Success = false, Error = new BatchItemError { Code = ErrorCodes.Unsupported, Message = $"project.add_member is currently not supported for C++ due to lack of cpp_add_member tool." } });
                batch.Failed++;
            }
            index++;
        }

        batch.Total = batch.Items.Count;
        return batch;
    }

    public async Task<InvestigateSymbolResult> ProjectInvestigateSymbolAsync(string symbol, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(symbol, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Classes.Count > 0 && searchRes.Classes[0].Location != null)
        {
            var file = searchRes.Classes[0].Location!.File;
            var lang = SourceLanguageDetector.FromPath(file);
            var isCsharp = lang == SourceLanguage.CSharp;
            var isCpp = lang == SourceLanguage.Cpp;

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                var codeRes = await CodeInvestigateAsync(symbol, 50, false, cancellationToken).ConfigureAwait(false);
                var result = new InvestigateSymbolResult { Name = symbol, Kind = "csharp" };
                if (codeRes.Symbol != null)
                {
                    result.Location = codeRes.Symbol.Location;
                    result.Declaration = codeRes.Symbol.Signature;
                }
                result.UsageCount = codeRes.Stats.ReferenceCount;
                return result;
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                var cppRes = await CppSymbolSummaryAsync(symbol, cancellationToken).ConfigureAwait(false);
                var cppResult = new InvestigateSymbolResult { Name = symbol, Kind = "cpp", Location = searchRes.Classes[0].Location };
                if (cppRes.Entries.Count > 0)
                {
                    cppResult.UsageCount = cppRes.Entries.Count;
                    cppResult.Declaration = cppRes.Entries[0].Decl.Signature;
                }
                return cppResult;
            }
        }

        throw new VsmcpException(ErrorCodes.NotFound, $"Could not find symbol {symbol} to investigate");
    }

    /// <summary>
    /// Caller graph for <c>className.methodName</c>, resolved through the Roslyn semantic model:
    /// each level is <see cref="Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindCallersAsync"/>
    /// on the previous level's calling symbols, so nodes are REAL enclosing members (the old
    /// implementation labeled callers "FileName.Line123"). C# only — for C++ use
    /// cpp_find_references_solution / cpp_investigate.
    /// </summary>
    public async Task<CallGraphResult> ProjectCallGraphAsync(string className, string methodName, int maxDepth, string? language, CancellationToken cancellationToken = default)
    {
        if (string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            throw new VsmcpException(ErrorCodes.Unsupported,
                "project.call_graph is C#-only. For C++ use cpp_find_references_solution or cpp_investigate.");
        if (maxDepth <= 0) maxDepth = 3;

        var ws = await GetWorkspaceAsync(cancellationToken);
        await TaskScheduler.Default; // semantic walk is heavy — keep it off the UI thread
        var solution = ws.CurrentSolution;

        // Resolve the starting member symbol.
        Microsoft.CodeAnalysis.ISymbol? target = null;
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var types = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindSourceDeclarationsAsync(
                project, n => string.Equals(n, className, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
            foreach (var t in types.OfType<Microsoft.CodeAnalysis.INamedTypeSymbol>())
            {
                target = t.GetMembers(methodName).FirstOrDefault();
                if (target is not null) break;
            }
            if (target is not null) break;
        }
        if (target is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"No member '{className}.{methodName}' found in the loaded solution.");

        var visited = new HashSet<string>();

        async Task<CallGraphNode> BuildAsync(Microsoft.CodeAnalysis.ISymbol sym, int depth)
        {
            var name = sym.ContainingType is null ? sym.Name : $"{sym.ContainingType.Name}.{sym.Name}";
            var node = new CallGraphNode { Name = name, Location = GetCodeSpan(sym) };

            // Cycle guard + depth cap: an already-expanded symbol appears as a leaf.
            if (!visited.Add(sym.ToDisplayString()) || depth >= maxDepth)
                return node;

            var callers = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindCallersAsync(
                sym, solution, cancellationToken).ConfigureAwait(false);
            foreach (var caller in callers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (caller.CallingSymbol is null) continue;
                node.CalledBy.Add(await BuildAsync(caller.CallingSymbol, depth + 1).ConfigureAwait(false));
            }
            return node;
        }

        return new CallGraphResult { Root = await BuildAsync(target, 0).ConfigureAwait(false) };
    }
}
