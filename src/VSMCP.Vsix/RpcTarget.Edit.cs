using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M15: Refactoring & Editing --------

    public async Task<(int Replacements, string Text)> EditReplaceAllAsync(
        string file, string pattern, string replacement, int? maxReplacements, bool regex,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await Task.Yield();

        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");
        var text = File.ReadAllText(file);
        int count = 0;
        string newText;

        if (regex)
        {
            Regex rx;
            try { rx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
            catch (ArgumentException ex) { throw new VsmcpException(ErrorCodes.NotFound, $"Invalid regex: {ex.Message}"); }
            newText = rx.Replace(text, m =>
            {
                if (maxReplacements is int max && count >= max) return m.Value;
                count++;
                return rx.Replace(m.Value, replacement, 1);
            });
        }
        else
        {
            int idx = 0;
            var sb = new System.Text.StringBuilder(text.Length);
            while (true)
            {
                if (maxReplacements is int max && count >= max)
                {
                    sb.Append(text, idx, text.Length - idx);
                    break;
                }
                int found = text.IndexOf(pattern, idx, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(text, idx, text.Length - idx);
                    break;
                }
                sb.Append(text, idx, found - idx);
                sb.Append(replacement);
                idx = found + pattern.Length;
                count++;
            }
            newText = sb.ToString();
        }

        if (count > 0)
            await FileWriteAsync(file, newText, cancellationToken).ConfigureAwait(false);
        return (count, newText);
    }

    public async Task<RenameResult> EditRenameAsync(
        string file, CodePosition position, string newName, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(newName)) throw new VsmcpException(ErrorCodes.NotFound, "newName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var sym = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        if (sym is null) throw new VsmcpException(ErrorCodes.NotFound, "No symbol at the specified position.");

        var result = new RenameResult();

        // Locate every reference for reporting first.
        var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
        foreach (var r in refs)
        {
            foreach (var loc in r.Locations)
            {
                var span = loc.Location.GetLineSpan();
                result.Locations.Add(new RenameLocation
                {
                    File = span.Path ?? "",
                    Line = span.StartLinePosition.Line + 1,
                    Column = span.StartLinePosition.Character + 1,
                    CurrentText = sym.Name,
                });
            }
        }

        if (dryRun) return result;

        var renameOptions = new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false);
        Solution newSolution;
        try
        {
            newSolution = await Renamer.RenameSymbolAsync(
                ws.CurrentSolution, sym, renameOptions, newName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.WrongState, $"Renamer.RenameSymbolAsync failed: {ex.Message}");
        }

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        try { RoslynEditor.Apply(ws, newSolution); }
        catch (VsmcpException) { throw; }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.WrongState, $"Apply failed: {ex.Message}");
        }
        return result;
    }

    public async Task<OrganizeUsingsResult> EditOrganizeUsingsAsync(
        string file, bool addMissing, bool removeUnused,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        // Roslyn provides Formatter for whitespace and OrganizeImports.OrganizeImportsService for ordering.
        // We approximate by:
        //   1) Sorting using-directives alphabetically (System first).
        //   2) (removeUnused) Diagnostic CS8019 'Unnecessary using directive' — removed via syntax rewrite.
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null) return new OrganizeUsingsResult();

        var originalUsings = root.Usings;
        var sorted = originalUsings
            .OrderBy(u => u.Name?.ToString().StartsWith("System", StringComparison.Ordinal) == true ? 0 : 1)
            .ThenBy(u => u.Name?.ToString(), StringComparer.Ordinal)
            .ToList();

        var keep = sorted.ToList();
        var removed = new List<string>();

        if (removeUnused)
        {
            var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (sm is not null)
            {
                var unnecessary = sm.GetDiagnostics(cancellationToken: cancellationToken)
                    .Where(d => d.Id == "CS8019")
                    .Select(d => d.Location.SourceSpan)
                    .ToHashSet();
                keep.RemoveAll(u =>
                {
                    if (unnecessary.Contains(u.Span)) { removed.Add(u.Name?.ToString() ?? ""); return true; }
                    return false;
                });
            }
        }

        var newRoot = root.WithUsings(SyntaxFactory.List(keep));
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var changed = !newRoot.IsEquivalentTo(root, topLevel: false);
        if (changed)
            RoslynEditor.ApplyDocumentChange(ws, doc, Formatter.Format(newRoot, ws));

        return new OrganizeUsingsResult
        {
            Changes = (changed ? 1 : 0) + (removed.Count > 0 ? 1 : 0),
            Added = new List<string>(),
            Removed = removed,
        };
    }

    public async Task<InsertResult> EditInsertBeforeAsync(
        string file, int line, string text, bool openInEditor,
        CancellationToken cancellationToken = default)
        => await InsertAtLineAsync(file, line, text, before: true, openInEditor, cancellationToken).ConfigureAwait(false);

    public async Task<InsertResult> EditInsertAfterAsync(
        string file, int line, string text, bool openInEditor,
        CancellationToken cancellationToken = default)
        => await InsertAtLineAsync(file, line, text, before: false, openInEditor, cancellationToken).ConfigureAwait(false);

    private async Task<InsertResult> InsertAtLineAsync(
        string file, int line, string text, bool before, bool openInEditor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (line < 1) line = 1;
        if (text is null) text = "";
        if (!text.EndsWith("\n", StringComparison.Ordinal)) text += "\n";

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var content = File.ReadAllText(file);
        var lines = content.Split('\n').ToList();

        int targetIdx = before ? line - 1 : line;
        targetIdx = Math.Max(0, Math.Min(targetIdx, lines.Count));
        var insertText = text.TrimEnd('\n');

        lines.Insert(targetIdx, insertText);
        var newContent = string.Join("\n", lines);

        await FileWriteAsync(file, newContent, cancellationToken).ConfigureAwait(false);

        if (openInEditor)
            await EditorOpenAsync(file, targetIdx + 1, 1, cancellationToken).ConfigureAwait(false);

        return new InsertResult { Line = targetIdx + 1, Text = text, OpenInEditor = openInEditor };
    }

    public async Task<ReplaceMemberResult> EditReplaceMemberAsync(
        string file, string className, string memberName, string newText, bool openInEditor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return new ReplaceMemberResult();

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, className, StringComparison.Ordinal));
        if (typeDecl is null) return new ReplaceMemberResult { Replaced = false };

        SyntaxNode? memberNode = typeDecl.DescendantNodes().FirstOrDefault(n =>
            (n is MethodDeclarationSyntax m && m.Identifier.Text == memberName)
            || (n is PropertyDeclarationSyntax p && p.Identifier.Text == memberName)
            || (n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.Text == memberName))
            || (n is EventDeclarationSyntax ev && ev.Identifier.Text == memberName)
            || (n is ConstructorDeclarationSyntax c && c.Identifier.Text == memberName));
        if (memberNode is null) return new ReplaceMemberResult { Replaced = false };

        var parsed = SyntaxFactory.ParseMemberDeclaration(newText);
        if (parsed is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse newText as a C# member declaration.");

        var newRoot = root.ReplaceNode(memberNode, parsed.WithTriviaFrom(memberNode));
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        RoslynEditor.ApplyDocumentChange(ws, doc, newRoot);

        var line = parsed.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (openInEditor)
            await EditorOpenAsync(file, line, 1, cancellationToken).ConfigureAwait(false);

        return new ReplaceMemberResult { Replaced = true, Line = line, OpenInEditor = openInEditor };
    }

    public async Task<MoveTypeResult> EditMoveTypeAsync(
        string file, string typeName, string? newNamespace, string? newFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(typeName)) throw new VsmcpException(ErrorCodes.NotFound, "typeName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return new MoveTypeResult();

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, typeName, StringComparison.Ordinal));
        if (typeDecl is null) return new MoveTypeResult { Success = false };

        var targetPath = newFile ?? Path.Combine(Path.GetDirectoryName(file)!, typeName + ".cs");
        if (File.Exists(targetPath))
            return new MoveTypeResult { Success = false, Conflict = true };

        // Build new file content: usings from source + namespace + type.
        var srcRoot = root as CompilationUnitSyntax;
        var usings = srcRoot?.Usings ?? default;

        BaseNamespaceDeclarationSyntax? srcNamespace = typeDecl.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var nsName = newNamespace ?? srcNamespace?.Name.ToString();

        SyntaxNode newFileRoot;
        if (!string.IsNullOrEmpty(nsName))
        {
            var ns = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(nsName!))
                .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(new[] { (MemberDeclarationSyntax)typeDecl.WithoutLeadingTrivia() }));
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(ns))
                .NormalizeWhitespace();
        }
        else
        {
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>((MemberDeclarationSyntax)typeDecl.WithoutLeadingTrivia()))
                .NormalizeWhitespace();
        }

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        File.WriteAllText(targetPath, newFileRoot.ToFullString());

        // Remove the type from the original file.
        var newSourceRoot = root.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;
        RoslynEditor.ApplyDocumentChange(ws, doc, newSourceRoot);

        return new MoveTypeResult
        {
            Success = true,
            NewLocation = new CodeSpan { File = targetPath, StartLine = 1, StartColumn = 1, EndLine = 1, EndColumn = 1 },
            Conflict = false,
        };
    }
}
