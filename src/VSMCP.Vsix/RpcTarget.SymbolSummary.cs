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
        public async Task<SymbolSummaryResult> CodeSymbolSummaryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");
            var match = await CodeFindSymbolAsync(symbol, kind: null, maxResults: 1, cancellationToken).ConfigureAwait(false);
            var first = match.Matches.FirstOrDefault();
            if (first is null)
                throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbol}");
            var result = new SymbolSummaryResult
            {
                Symbol = first
            };
            if (first.Location is null)
                return result;
            var ws = await GetWorkspaceAsync(cancellationToken);
            var doc = FindDocument(ws.CurrentSolution, first.Location.File);
            if (doc is null)
                return result;
            var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (sm is null || root is null)
                return result;
            var pos = first.Location;
            var node = root.DescendantNodes().OfType<MemberDeclarationSyntax>().FirstOrDefault(n =>
            {
                var s = n.GetLocation().GetLineSpan().StartLinePosition;
                return s.Line + 1 == pos.StartLine;
            });
            if (node is null)
                return result;
            var spanLines = node.GetLocation().GetLineSpan();
            result.LineCount = spanLines.EndLinePosition.Line - spanLines.StartLinePosition.Line + 1;
            if (node is MethodDeclarationSyntax method)
            {
                var declSym = sm.GetDeclaredSymbol(method) as IMethodSymbol;
                result.IsAsync = declSym?.IsAsync ?? false;
                result.Returns = declSym?.ReturnType.ToDisplayString();
                // Calls: invocations
                var calls = new HashSet<string>(StringComparer.Ordinal);
                foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var s = sm.GetSymbolInfo(inv).Symbol;
                    if (s is null)
                        continue;
                    calls.Add(s.ToDisplayString(CtxHelpers.ToFormat(SymbolDisplayMode.Qualified)));
                    if (calls.Count >= 30)
                        break;
                }

                result.Calls = calls.ToList();
                // Touches: field references
                var touches = new HashSet<string>(StringComparer.Ordinal);
                foreach (var ident in method.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    var s = sm.GetSymbolInfo(ident).Symbol;
                    if (s is IFieldSymbol f && SymbolEqualityComparer.Default.Equals(f.ContainingType, declSym?.ContainingType))
                        touches.Add(f.Name);
                }

                result.Touches = touches.ToList();
                // Throws
                foreach (var t in method.DescendantNodes().OfType<ThrowStatementSyntax>())
                {
                    var s = sm.GetTypeInfo(t.Expression!).Type;
                    if (s is not null)
                        result.Throws.Add(s.Name);
                }

                foreach (var t in method.DescendantNodes().OfType<ThrowExpressionSyntax>())
                {
                    var s = sm.GetTypeInfo(t.Expression).Type;
                    if (s is not null)
                        result.Throws.Add(s.Name);
                }

                result.Throws = result.Throws.Distinct().ToList();
                // Cyclomatic + awaits
                int cyc = 1;
                foreach (var _ in method.DescendantNodes().OfType<IfStatementSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<SwitchSectionSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<WhileStatementSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<ForStatementSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<ForEachStatementSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<CatchClauseSyntax>())
                    cyc++;
                foreach (var _ in method.DescendantNodes().OfType<ConditionalExpressionSyntax>())
                    cyc++;
                cyc += method.DescendantTokens().Count(t => t.IsKind(SyntaxKind.AmpersandAmpersandToken) || t.IsKind(SyntaxKind.BarBarToken));
                result.Cyclomatic = cyc;
                result.Awaits = method.DescendantNodes().OfType<AwaitExpressionSyntax>().Count();
            }

            return result;
        }
    }
}