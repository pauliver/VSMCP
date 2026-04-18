using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
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
        var doc = FindDocument(ws.CurrentSolution, file)
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
        var doc = FindDocument(ws.CurrentSolution, position.File)
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
        var doc = FindDocument(ws.CurrentSolution, position.File)
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
        var doc = FindDocument(ws.CurrentSolution, position.File)
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
}
