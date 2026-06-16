using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    private async Task<VisualStudioWorkspace> GetWorkspaceAsync(CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        if (await _package.GetServiceAsync(typeof(SComponentModel)) is not IComponentModel cm)
            throw new VsmcpException(ErrorCodes.InteropFault, "IComponentModel unavailable.");
        var ws = cm.GetService<VisualStudioWorkspace>()
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "VisualStudioWorkspace unavailable.");
        return ws;
    }

    private static Document? FindDocument(Solution solution, string filePath)
    {
        var full = System.IO.Path.GetFullPath(filePath);
        var id = solution.GetDocumentIdsWithFilePath(full).FirstOrDefault();
        return id is null ? null : solution.GetDocument(id);
    }

    /// <summary>
    /// FindDocument with sidecar fall-through for Open Folder mode. When the live
    /// VisualStudioWorkspace doesn't have the file (no .sln loaded, csproj-as-MiscFile, etc.),
    /// look it up in the WorkspaceSidecar's AdhocWorkspace if a project has been loaded there
    /// via project.load_workspace_folder.
    /// </summary>
    internal Document? FindDocumentAnywhere(Solution solution, string filePath)
    {
        return FindDocument(solution, filePath) ?? FindDocumentInSidecar(filePath);
    }

    private static int PositionFromLineCol(SourceText text, int line, int column)
    {
        if (line < 1) line = 1;
        if (column < 1) column = 1;
        var lineIndex = Math.Min(line - 1, text.Lines.Count - 1);
        var ln = text.Lines[lineIndex];
        var col = Math.Min(column - 1, ln.End - ln.Start);
        return ln.Start + col;
    }

    private static CodeSpan SpanFromLocation(Location loc)
    {
        var span = loc.GetLineSpan();
        return new CodeSpan
        {
            File = span.Path ?? "",
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
        };
    }

    private static CodeSymbol ToCodeSymbol(ISymbol symbol)
    {
        var cs = new CodeSymbol
        {
            Name = symbol.Name,
            Kind = symbol.Kind.ToString(),
            ContainerName = symbol.ContainingSymbol?.ToDisplayString(),
            Signature = symbol.ToDisplayString(),
        };
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is not null) cs.Location = SpanFromLocation(loc);
        return cs;
    }

    public async Task<SymbolsResult> CodeSymbolsAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {file}");

        var result = new SymbolsResult { File = file, Language = doc.Project.Language };
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || sm is null) return result;

        foreach (var node in root.ChildNodes())
            WalkOutline(node, sm, result.Symbols, cancellationToken);
        return result;
    }

    private static void WalkOutline(SyntaxNode node, SemanticModel sm, List<CodeSymbol> into, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (node)
        {
            case BaseNamespaceDeclarationSyntax ns:
            {
                var nsSym = sm.GetDeclaredSymbol(ns);
                var entry = nsSym is not null ? ToCodeSymbol(nsSym) : new CodeSymbol { Name = ns.Name.ToString(), Kind = "Namespace" };
                entry.Location ??= SpanFromLocation(ns.GetLocation());
                foreach (var c in ns.ChildNodes()) WalkOutline(c, sm, entry.Children, ct);
                into.Add(entry);
                return;
            }
            case BaseTypeDeclarationSyntax type:
            {
                var sym = sm.GetDeclaredSymbol(type);
                var entry = sym is not null ? ToCodeSymbol(sym) : new CodeSymbol { Name = type.Identifier.Text, Kind = "NamedType" };
                entry.Location ??= SpanFromLocation(type.GetLocation());
                foreach (var member in type.ChildNodes()) WalkOutline(member, sm, entry.Children, ct);
                into.Add(entry);
                return;
            }
            case DelegateDeclarationSyntax del:
            {
                var sym = sm.GetDeclaredSymbol(del);
                if (sym is not null) into.Add(ToCodeSymbol(sym));
                return;
            }
            case BaseMethodDeclarationSyntax m:
            {
                var sym = sm.GetDeclaredSymbol(m);
                if (sym is not null) into.Add(ToCodeSymbol(sym));
                return;
            }
            case PropertyDeclarationSyntax p:
            {
                var sym = sm.GetDeclaredSymbol(p);
                if (sym is not null) into.Add(ToCodeSymbol(sym));
                return;
            }
            case EventDeclarationSyntax ev:
            {
                var sym = sm.GetDeclaredSymbol(ev);
                if (sym is not null) into.Add(ToCodeSymbol(sym));
                return;
            }
            case FieldDeclarationSyntax fld:
            {
                foreach (var v in fld.Declaration.Variables)
                {
                    var sym = sm.GetDeclaredSymbol(v);
                    if (sym is not null) into.Add(ToCodeSymbol(sym));
                }
                return;
            }
            case EventFieldDeclarationSyntax ef:
            {
                foreach (var v in ef.Declaration.Variables)
                {
                    var sym = sm.GetDeclaredSymbol(v);
                    if (sym is not null) into.Add(ToCodeSymbol(sym));
                }
                return;
            }
        }
    }

    public async Task<LocationListResult> CodeGotoDefinitionAsync(CodePosition position, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        var result = new LocationListResult();
        if (symbol is null) return result;
        result.Symbol = ToCodeSymbol(symbol);
        foreach (var loc in symbol.Locations)
            if (loc.IsInSource) result.Locations.Add(SpanFromLocation(loc));
        return result;
    }

    public async Task<ReferencesResult> CodeFindReferencesAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        if (maxResults <= 0) maxResults = 500;
        if (maxResults > 5000) maxResults = 5000;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        var result = new ReferencesResult();
        if (symbol is null) return result;
        result.Symbol = ToCodeSymbol(symbol);

        foreach (var loc in symbol.Locations)
            if (loc.IsInSource) result.Definitions.Add(SpanFromLocation(loc));

        var refs = await SymbolFinder.FindReferencesAsync(symbol, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
        int total = 0;
        foreach (var group in refs)
        {
            foreach (var rl in group.Locations)
            {
                total++;
                if (result.References.Count < maxResults)
                    result.References.Add(SpanFromLocation(rl.Location));
            }
        }
        result.TotalReferences = total;
        result.Truncated = total > result.References.Count;
        return result;
    }

    public async Task<DiagnosticsResult> CodeDiagnosticsAsync(string? file, int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0) maxResults = 500;
        if (maxResults > 10_000) maxResults = 10_000;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var solution = ws.CurrentSolution;
        var result = new DiagnosticsResult();
        int total = 0;

        if (!string.IsNullOrWhiteSpace(file))
        {
            var doc = FindDocument(solution, file!)
                ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {file}");
            result.FilesScanned.Add(doc.FilePath ?? file!);
            var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (sm is not null)
            {
                foreach (var d in sm.GetDiagnostics(cancellationToken: cancellationToken))
                {
                    total++;
                    if (result.Diagnostics.Count < maxResults)
                        result.Diagnostics.Add(ToDiagInfo(d));
                }
            }
        }
        else
        {
            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null) continue;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!string.IsNullOrEmpty(tree.FilePath)) result.FilesScanned.Add(tree.FilePath);
                }
                foreach (var d in compilation.GetDiagnostics(cancellationToken))
                {
                    total++;
                    if (result.Diagnostics.Count < maxResults)
                        result.Diagnostics.Add(ToDiagInfo(d));
                }
            }
        }

        result.TotalDiagnostics = total;
        result.Truncated = total > result.Diagnostics.Count;
        return result;
    }

    private static CodeDiagnosticInfo ToDiagInfo(Diagnostic d) => new()
    {
        Id = d.Id,
        Severity = d.Severity switch
        {
            DiagnosticSeverity.Error => CodeDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => CodeDiagnosticSeverity.Warning,
            DiagnosticSeverity.Info => CodeDiagnosticSeverity.Info,
            _ => CodeDiagnosticSeverity.Hidden,
        },
        Message = d.GetMessage(),
        Category = d.Descriptor.Category,
        Location = d.Location != Location.None ? SpanFromLocation(d.Location) : null,
    };

    public async Task<QuickInfoResult> CodeQuickInfoAsync(CodePosition position, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        var result = new QuickInfoResult();
        if (symbol is null) return result;

        result.Symbol = ToCodeSymbol(symbol);
        result.Signature = symbol.ToDisplayString();
        result.Kind = symbol.Kind.ToString();
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml)) result.Documentation = xml;
        return result;
    }

    /// <summary>
    /// Call hierarchy at a position (#139). direction "callers" uses SymbolFinder.FindCallersAsync;
    /// "callees" walks the symbol's declaration body for invoked symbols.
    /// </summary>
    public async Task<CallHierarchyResult> CodeCallHierarchyAsync(CodePosition position, string direction, int maxResults, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        if (maxResults <= 0) maxResults = 200;
        if (maxResults > 2000) maxResults = 2000;
        var callees = string.Equals(direction, "callees", StringComparison.OrdinalIgnoreCase);

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");
        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        var result = new CallHierarchyResult { Direction = callees ? "callees" : "callers" };
        if (symbol is null) return result;
        result.Symbol = symbol.ToDisplayString();

        if (!callees)
        {
            var callers = await SymbolFinder.FindCallersAsync(symbol, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
            foreach (var c in callers)
            {
                if (result.Calls.Count >= maxResults) { result.Truncated = true; break; }
                var entry = new CallHierarchyEntry
                {
                    Name = c.CallingSymbol.Name,
                    ContainerName = c.CallingSymbol.ContainingSymbol?.ToDisplayString(),
                    Signature = c.CallingSymbol.ToDisplayString(),
                };
                foreach (var loc in c.Locations)
                    if (loc.IsInSource) entry.Locations.Add(SpanFromLocation(loc));
                result.Calls.Add(entry);
            }
        }
        else
        {
            var seen = new HashSet<string>();
            foreach (var sref in symbol.DeclaringSyntaxReferences)
            {
                var node = await sref.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                var sm = await doc.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is { } comp
                    ? comp.GetSemanticModel(node.SyntaxTree) : null;
                if (sm is null) continue;
                foreach (var inv in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var callee = sm.GetSymbolInfo(inv, cancellationToken).Symbol;
                    if (callee is null || !seen.Add(callee.ToDisplayString())) continue;
                    if (result.Calls.Count >= maxResults) { result.Truncated = true; break; }
                    var entry = new CallHierarchyEntry
                    {
                        Name = callee.Name,
                        ContainerName = callee.ContainingSymbol?.ToDisplayString(),
                        Signature = callee.ToDisplayString(),
                    };
                    foreach (var loc in callee.Locations)
                        if (loc.IsInSource) entry.Locations.Add(SpanFromLocation(loc));
                    result.Calls.Add(entry);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Public API surface of the type at a position (#139), including metadata/framework types
    /// that goto_definition can't open. Returns the type's public members' signatures.
    /// </summary>
    public async Task<TypeSurfaceResult> CodeTypeSurfaceAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        if (maxResults <= 0) maxResults = 300;
        if (maxResults > 3000) maxResults = 3000;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");
        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        var result = new TypeSurfaceResult();
        if (symbol is null) return result;

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type is null) return result;
        result.Type = type.ToDisplayString();
        result.Assembly = type.ContainingAssembly?.Name;
        result.FromMetadata = !type.Locations.Any(l => l.IsInSource);

        foreach (var m in type.GetMembers())
        {
            if (m.DeclaredAccessibility != Accessibility.Public || !m.CanBeReferencedByName) continue;
            if (result.Members.Count >= maxResults) { result.Truncated = true; break; }
            result.Members.Add(m.ToDisplayString());
        }
        return result;
    }

    /// <summary>
    /// Completion candidates at a position (#138). For `expr.` member access, lists the
    /// accessible members of expr's type (including inherited); otherwise lists symbols in
    /// scope via the semantic model. Uses only public Workspaces APIs (no EditorFeatures).
    /// </summary>
    public async Task<CompletionResult> CodeCompleteAsync(CodePosition position, int maxResults, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        if (maxResults <= 0) maxResults = 100;
        if (maxResults > 500) maxResults = 500;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");
        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var result = new CompletionResult();
        if (root is null || sm is null) return result;

        var token = root.FindToken(pos > 0 ? pos - 1 : 0);
        var memberAccess = token.Parent?.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(m => m.OperatorToken.Span.End <= pos);

        var symbols = new List<ISymbol>();
        if (memberAccess is not null)
        {
            var type = sm.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            for (var t = type; t is not null; t = t.BaseType)
                symbols.AddRange(t.GetMembers().Where(m => m.CanBeReferencedByName && m.DeclaredAccessibility == Accessibility.Public));
        }
        else
        {
            symbols.AddRange(sm.LookupSymbols(pos).Where(s => s.CanBeReferencedByName));
        }

        var seen = new HashSet<string>();
        foreach (var s in symbols)
        {
            if (!seen.Add(s.Name)) continue;
            if (result.Items.Count >= maxResults) { result.Truncated = true; break; }
            result.Items.Add(new CompletionEntry { Name = s.Name, Kind = s.Kind.ToString(), Signature = s.ToDisplayString() });
        }
        return result;
    }

    /// <summary>
    /// Signature help (#138): the overload signatures for the invocation enclosing the position,
    /// plus the active parameter index (commas before the caret in the argument list).
    /// </summary>
    public async Task<SignatureHelpResult> CodeSignatureHelpAsync(CodePosition position, CancellationToken cancellationToken = default)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocumentAnywhere(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");
        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var result = new SignatureHelpResult();
        if (root is null || sm is null) return result;

        var token = root.FindToken(pos > 0 ? pos - 1 : 0);
        var invocation = token.Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation is null) return result;

        var info = sm.GetSymbolInfo(invocation, cancellationToken);
        var candidates = new List<ISymbol>();
        if (info.Symbol is not null) candidates.Add(info.Symbol);
        candidates.AddRange(info.CandidateSymbols);
        var seen = new HashSet<string>();
        foreach (var c in candidates)
        {
            var sig = c.ToDisplayString();
            if (seen.Add(sig)) result.Signatures.Add(sig);
        }

        if (invocation.ArgumentList is not null)
        {
            int active = 0;
            foreach (var sep in invocation.ArgumentList.Arguments.GetSeparators())
                if (sep.Span.End <= pos) active++;
            result.ActiveParameter = active;
        }
        return result;
    }

    /// <summary>
    /// Format a document (or a range within it) with the Roslyn Formatter, honoring the
    /// project's .editorconfig (#136). Applied through Workspace.TryApplyChanges so the edit is
    /// grouped with VS undo and reflected in open buffers.
    /// </summary>
    public async Task<FormatResult> CodeFormatAsync(string file, FileRange? range, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {file}");

        var oldText = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        Document formatted;
        if (range is not null)
        {
            var start = PositionFromLineCol(oldText, range.StartLine, range.StartColumn);
            var end = PositionFromLineCol(oldText, range.EndLine, range.EndColumn);
            if (end < start) (start, end) = (end, start);
            formatted = await Formatter.FormatAsync(doc, TextSpan.FromBounds(start, end), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            formatted = await Formatter.FormatAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var newText = await formatted.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var changed = !newText.ContentEquals(oldText);
        if (changed && !ws.TryApplyChanges(formatted.Project.Solution))
            throw new VsmcpException(ErrorCodes.WorkspaceLocked, "Workspace rejected the format edit (TryApplyChanges returned false).");

        return new FormatResult { File = file, Changed = changed };
    }
}
