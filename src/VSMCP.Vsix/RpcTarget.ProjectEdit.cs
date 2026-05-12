using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using VSMCP.Shared;

namespace VSMCP.Vsix;

public partial class RpcTarget
{
    public async Task<ReplaceMemberResult> ProjectReplaceMemberAsync(string className, string memberName, string newCode, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(className, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Matches.Count > 0 && searchRes.Matches[0].Location != null)
        {
            var file = searchRes.Matches[0].Location!.File;
            var isCpp = file.EndsWith(".h") || file.EndsWith(".cpp") || file.EndsWith(".hpp") || file.EndsWith(".cc") || file.EndsWith(".cxx");
            var isCsharp = file.EndsWith(".cs");

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                return await EditReplaceMemberAsync(file, className, memberName, newCode, false, cancellationToken).ConfigureAwait(false);
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                var res = await CppReplaceMemberAsync(file, className, memberName, newCode, cancellationToken).ConfigureAwait(false);
                return new ReplaceMemberResult { Replaced = res.Replaced, Line = res.Line };
            }

            throw new RpcException(VSMCP.Shared.ErrorCodes.NotFound, $"Could not auto-detect language for file {file}");
        }

        throw new RpcException(VSMCP.Shared.ErrorCodes.NotFound, $"Could not find class {className}");
    }

    public async Task<AddMemberResult> ProjectAddMemberAsync(string className, string newCode, string? insertBefore, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(className, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Matches.Count > 0 && searchRes.Matches[0].Location != null)
        {
            var file = searchRes.Matches[0].Location!.File;
            var isCpp = file.EndsWith(".h") || file.EndsWith(".cpp") || file.EndsWith(".hpp") || file.EndsWith(".cc") || file.EndsWith(".cxx");
            var isCsharp = file.EndsWith(".cs");

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                return await EditAddMemberAsync(file, className, newCode, insertBefore, false, cancellationToken).ConfigureAwait(false);
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                throw new RpcException(VSMCP.Shared.ErrorCodes.Unsupported, $"project.add_member is currently not supported for C++ due to lack of cpp_add_member");
            }

            throw new RpcException(VSMCP.Shared.ErrorCodes.NotFound, $"Could not auto-detect language for file {file}");
        }

        throw new RpcException(VSMCP.Shared.ErrorCodes.NotFound, $"Could not find class {className} for adding member");
    }

    public async Task<BatchResult<AddMemberResult>> ProjectAddMemberToSubclassesAsync(string baseType, string newCode, string? language, CancellationToken cancellationToken = default)
    {
        // Find all subclasses / implementations
        var searchRes = await SearchClassesAsync(null, baseType, baseType, 500, cancellationToken).ConfigureAwait(false);

        var batch = new BatchResult<AddMemberResult>();
        foreach (var match in searchRes.Matches)
        {
            if (match.Location == null) continue;
            var className = match.Name;
            var file = match.Location.File;

            var isCsharp = file.EndsWith(".cs");
            var isCpp = file.EndsWith(".h") || file.EndsWith(".cpp") || file.EndsWith(".hpp") || file.EndsWith(".cc") || file.EndsWith(".cxx");

            if (isCsharp)
            {
                try
                {
                    var res = await EditAddMemberAsync(file, className, newCode, null, false, cancellationToken).ConfigureAwait(false);
                    batch.Results.Add(res);
                }
                catch (Exception ex)
                {
                    batch.Errors.Add(new BatchItemError { Message = ex.Message });
                }
            }
            else if (isCpp)
            {
                 batch.Errors.Add(new BatchItemError { Message = $"project.add_member is currently not supported for C++ due to lack of cpp_add_member tool." });
            }
        }

        return batch;
    }

    public async Task<InvestigateSymbolResult> ProjectInvestigateSymbolAsync(string symbol, string? language, CancellationToken cancellationToken = default)
    {
        var searchRes = await SearchClassesAsync(symbol, null, null, 1, cancellationToken).ConfigureAwait(false);
        if (searchRes.Matches.Count > 0 && searchRes.Matches[0].Location != null)
        {
            var file = searchRes.Matches[0].Location!.File;
            var isCpp = file.EndsWith(".h") || file.EndsWith(".cpp") || file.EndsWith(".hpp") || file.EndsWith(".cc") || file.EndsWith(".cxx");
            var isCsharp = file.EndsWith(".cs");

            if ((language == null && isCsharp) || string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                var codeRes = await CodeInvestigateAsync(symbol, 50, false, cancellationToken).ConfigureAwait(false);
                var result = new InvestigateSymbolResult { Name = symbol, Kind = "csharp" };
                if (codeRes.Symbol != null)
                {
                    result.Location = codeRes.Symbol.Location;
                    result.Declaration = codeRes.Symbol.Signature;
                }
                if (codeRes.Callers != null)
                    result.UsageCount = codeRes.Callers.Count;
                return result;
            }

            if ((language == null && isCpp) || string.Equals(language, "cpp", StringComparison.OrdinalIgnoreCase))
            {
                 var cppRes = await CppSymbolSummaryAsync(symbol, cancellationToken).ConfigureAwait(false);
                 var cppResult = new InvestigateSymbolResult { Name = symbol, Kind = "cpp", Location = searchRes.Matches[0].Location };
                 if (cppRes.Matches.Count > 0)
                 {
                     cppResult.UsageCount = cppRes.Matches.Count;
                     cppResult.Declaration = cppRes.Matches[0].Signature;
                 }
                 return cppResult;
            }
        }

        throw new RpcException(VSMCP.Shared.ErrorCodes.NotFound, $"Could not find symbol {symbol} to investigate");
    }

    public async Task<CallGraphResult> ProjectCallGraphAsync(string className, string methodName, int maxDepth, string? language, CancellationToken cancellationToken = default)
    {
        var result = new CallGraphResult();
        var visited = new HashSet<string>();

        async Task<CallGraphNode> TraceCallsAsync(string clzName, string methName, int currentDepth)
        {
            var nodeName = $"{clzName}.{methName}";
            var node = new CallGraphNode { Name = nodeName };

            if (visited.Contains(nodeName)) return node;
            visited.Add(nodeName);

            var searchRes = await SearchMembersAsync(methName, null, clzName, cancellationToken).ConfigureAwait(false);
            if (searchRes.Members.Count > 0)
            {
                node.Location = searchRes.Members[0].Location;

                if (node.Location != null && currentDepth < maxDepth)
                {
                    var usages = await SearchFindUsagesAsync(node.Location.File, new CodePosition { Line = node.Location.Line, Column = node.Location.Column }, cancellationToken).ConfigureAwait(false);
                    foreach (var usage in usages.Usages)
                    {
                        if (usage.Type == "reference")
                        {
                            CallGraphNode childNode = null;
                            if (currentDepth + 1 < maxDepth)
                            {
                                // Attempt to parse out the class/method name from the usage file
                                // As a placeholder, we use the file name as class, and line number as method.
                                // A proper AST parser would be needed here to resolve the actual calling method.
                                var callingClass = System.IO.Path.GetFileNameWithoutExtension(usage.File);
                                var callingMethod = $"Line{usage.Line}";
                                childNode = await TraceCallsAsync(callingClass, callingMethod, currentDepth + 1).ConfigureAwait(false);
                            }
                            else
                            {
                                childNode = new CallGraphNode { Name = $"{usage.File}:{usage.Line}", Location = new CodeSpan { File = usage.File, Line = usage.Line, Column = usage.Column }};
                            }

                            node.CalledBy.Add(childNode);
                        }
                    }
                }
            }
            return node;
        }

        result.Root = await TraceCallsAsync(className, methodName, 0).ConfigureAwait(false);
        return result;
    }
}
