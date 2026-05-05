using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix
{
    internal sealed partial class RpcTarget
    {
        public async Task<InvestigateResult> CodeInvestigateAsync(string symbol, int maxRefs, bool includeTests, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");
            if (maxRefs <= 0)
                maxRefs = 50;
            var matches = await CodeFindSymbolAsync(symbol, kind: null, maxResults: 1, cancellationToken).ConfigureAwait(false);
            var first = matches.Matches.FirstOrDefault();
            if (first is null)
                throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbol}");
            var result = new InvestigateResult
            {
                Symbol = first
            };
            if (first.Location is null)
                return result;
            var ws = await GetWorkspaceAsync(cancellationToken);
            var doc = FindDocumentAnywhere(ws.CurrentSolution, first.Location.File);
            if (doc is null)
                return result;
            var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (sm is null || root is null)
                return result;
            // Body
            var declNode = root.DescendantNodes().OfType<MemberDeclarationSyntax>().FirstOrDefault(n => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == first.Location.StartLine);
            if (declNode is not null)
                result.Body = declNode.ToFullString();
            // Symbol stats
            var sym = declNode is null ? null : sm.GetDeclaredSymbol(declNode);
            if (sym is not null)
            {
                result.Stats.IsStatic = sym.IsStatic;
                result.Stats.IsAbstract = sym.IsAbstract;
                result.Stats.IsVirtual = sym.IsVirtual;
                if (sym is IMethodSymbol ms)
                    result.Stats.IsAsync = ms.IsAsync;
            }

            // Calls (in)
            if (sym is not null)
            {
                try
                {
                    var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
                    int count = 0;
                    foreach (var refResult in refs)
                    {
                        foreach (var loc in refResult.Locations)
                        {
                            if (count >= maxRefs)
                                break;
                            var span = loc.Location.GetLineSpan();
                            result.Calls.Add(new InvestigateCallEntry { Symbol = "", Location = new CodeSpan { File = span.Path ?? "", StartLine = span.StartLinePosition.Line + 1, StartColumn = span.StartLinePosition.Character + 1, EndLine = span.EndLinePosition.Line + 1, EndColumn = span.EndLinePosition.Character + 1, }, });
                            count++;
                        }

                        if (count >= maxRefs)
                            break;
                    }

                    result.Stats.ReferenceCount = count;
                }
                catch
                {
                }
            }

            // Calls (out): invocations inside body
            if (declNode is not null)
            {
                foreach (var inv in declNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var s = sm.GetSymbolInfo(inv).Symbol;
                    if (s is null)
                        continue;
                    var span = inv.GetLocation().GetLineSpan();
                    result.CallsOut.Add(new InvestigateCallEntry { Symbol = s.ToDisplayString(CtxHelpers.ToFormat(SymbolDisplayMode.Qualified)), Location = new CodeSpan { File = span.Path ?? "", StartLine = span.StartLinePosition.Line + 1, StartColumn = span.StartLinePosition.Character + 1, EndLine = span.EndLinePosition.Line + 1, EndColumn = span.EndLinePosition.Character + 1, }, });
                }
            }

            // Tests: heuristic â€” references whose containing type's name contains "Test"
            if (includeTests && sym is not null)
            {
                try
                {
                    var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
                    foreach (var refResult in refs)
                    {
                        foreach (var loc in refResult.Locations)
                        {
                            var docId = loc.Document.Id;
                            var refDoc = ws.CurrentSolution.GetDocument(docId);
                            if (refDoc is null)
                                continue;
                            var refRoot = await refDoc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                            if (refRoot is null)
                                continue;
                            var node = refRoot.FindNode(loc.Location.SourceSpan);
                            var containingType = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                            if (containingType is null)
                                continue;
                            var name = containingType.Identifier.Text;
                            if (name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Spec", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            var span = containingType.GetLocation().GetLineSpan();
                            result.Tests.Add(new InvestigateCallEntry { Symbol = name, Location = new CodeSpan { File = span.Path ?? "", StartLine = span.StartLinePosition.Line + 1, StartColumn = span.StartLinePosition.Character + 1, EndLine = span.EndLinePosition.Line + 1, EndColumn = span.EndLinePosition.Character + 1, }, });
                            if (result.Tests.Count >= 10)
                                break;
                        }

                        if (result.Tests.Count >= 10)
                            break;
                    }
                }
                catch
                {
                }
            }

            return result;
        }
    }
}