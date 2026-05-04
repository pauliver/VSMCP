using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M18: Semantic Code Layer — symbol lookup, member read/add, wrappers --------

    public async Task<SymbolMatchResult> CodeFindSymbolAsync(
        string name, string? kind, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name)) throw new VsmcpException(ErrorCodes.NotFound, "name is required.");
        if (maxResults <= 0) maxResults = 100;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var result = new SymbolMatchResult();

        // Split "Class.Member" into parts; if dotted, use as qualified-name hint.
        var simpleName = name.Contains('.') ? name.Split('.').Last() : name;

        foreach (var project in ws.CurrentSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hits = await SymbolFinder.FindDeclarationsAsync(
                project, simpleName, ignoreCase: true, cancellationToken).ConfigureAwait(false);
            foreach (var sym in hits)
            {
                if (kind is not null && !string.Equals(sym.Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase)) continue;

                var qn = sym.ToDisplayString();
                if (name.Contains('.')
                    && !qn.EndsWith(name, StringComparison.OrdinalIgnoreCase)
                    && qn.IndexOf("." + name, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                result.Matches.Add(new SymbolMatch
                {
                    Name = sym.Name,
                    QualifiedName = qn,
                    Kind = sym.Kind.ToString().ToLowerInvariant(),
                    Signature = sym.ToDisplayString(),
                    Location = GetCodeSpan(sym),
                    Container = sym.ContainingSymbol?.ToDisplayString(),
                });
                if (result.Matches.Count >= maxResults)
                {
                    result.Truncated = true;
                    break;
                }
            }
            if (result.Matches.Count >= maxResults) break;
        }

        result.Total = result.Matches.Count;
        return result;
    }

    public async Task<ReadMemberResult> CodeReadMemberAsync(
        string? file, string className, string memberName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(memberName)) throw new VsmcpException(ErrorCodes.NotFound, "memberName is required.");

        var resolvedFile = file;
        if (string.IsNullOrEmpty(resolvedFile))
        {
            var match = await CodeFindSymbolAsync(className + "." + memberName, kind: null, maxResults: 5, cancellationToken).ConfigureAwait(false);
            resolvedFile = match.Matches.FirstOrDefault()?.Location?.File;
            if (string.IsNullOrEmpty(resolvedFile))
                throw new VsmcpException(ErrorCodes.NotFound, $"Could not locate {className}.{memberName} in solution.");
        }

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, resolvedFile!)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {resolvedFile}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || sm is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);
        if (typeDecl is null) throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {resolvedFile}.");

        SyntaxNode? memberNode = typeDecl.DescendantNodes().FirstOrDefault(n =>
            (n is MethodDeclarationSyntax m && m.Identifier.Text == memberName)
            || (n is PropertyDeclarationSyntax p && p.Identifier.Text == memberName)
            || (n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.Text == memberName))
            || (n is EventDeclarationSyntax ev && ev.Identifier.Text == memberName)
            || (n is ConstructorDeclarationSyntax c && c.Identifier.Text == memberName));
        if (memberNode is null) throw new VsmcpException(ErrorCodes.NotFound, $"Member '{memberName}' not in {className}.");

        var memberSym = sm.GetDeclaredSymbol(memberNode);
        var span = memberNode.GetLocation().GetLineSpan();
        return new ReadMemberResult
        {
            File = resolvedFile!,
            Content = memberNode.ToFullString(),
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            Signature = memberSym?.ToDisplayString(),
        };
    }

    public async Task<AddMemberResult> EditAddMemberAsync(
        string? file, string className, string memberCode, string? insertBefore, bool openInEditor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(memberCode)) throw new VsmcpException(ErrorCodes.NotFound, "memberCode is required.");

        var resolvedFile = file;
        if (string.IsNullOrEmpty(resolvedFile))
        {
            var match = await CodeFindSymbolAsync(className, "namedtype", 5, cancellationToken).ConfigureAwait(false);
            resolvedFile = match.Matches.FirstOrDefault()?.Location?.File;
            if (string.IsNullOrEmpty(resolvedFile))
                throw new VsmcpException(ErrorCodes.NotFound, $"Could not locate class {className} in solution.");
        }

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, resolvedFile!)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {resolvedFile}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);
        if (typeDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {resolvedFile}.");

        var newMember = SyntaxFactory.ParseMemberDeclaration(memberCode);
        if (newMember is null)
            throw new VsmcpException(ErrorCodes.NotFound, "memberCode could not be parsed as a C# member declaration.");

        SyntaxNode newRoot;
        int insertedAtLine;

        if (typeDecl is TypeDeclarationSyntax td)
        {
            MemberDeclarationSyntax? before = null;
            if (!string.IsNullOrEmpty(insertBefore))
            {
                before = td.Members.FirstOrDefault(m =>
                    (m is MethodDeclarationSyntax mm && mm.Identifier.Text == insertBefore)
                    || (m is PropertyDeclarationSyntax pp && pp.Identifier.Text == insertBefore)
                    || (m is FieldDeclarationSyntax ff && ff.Declaration.Variables.Any(v => v.Identifier.Text == insertBefore)));
            }

            TypeDeclarationSyntax newTd;
            if (before is not null)
            {
                var idx = td.Members.IndexOf(before);
                newTd = td.WithMembers(td.Members.Insert(idx, (MemberDeclarationSyntax)newMember));
                insertedAtLine = before.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            else
            {
                newTd = td.WithMembers(td.Members.Add((MemberDeclarationSyntax)newMember));
                insertedAtLine = td.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            newRoot = root.ReplaceNode(td, newTd);
        }
        else
        {
            throw new VsmcpException(ErrorCodes.Unsupported, "Only class/struct/record/interface types support member addition (not enum/delegate).");
        }

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        RoslynEditor.ApplyDocumentChange(ws, doc, newRoot);

        if (openInEditor)
            await EditorOpenAsync(resolvedFile!, insertedAtLine, 1, cancellationToken).ConfigureAwait(false);

        return new AddMemberResult
        {
            File = resolvedFile!,
            ClassName = className,
            InsertedAtLine = insertedAtLine,
            OpenInEditor = openInEditor,
        };
    }

    public async Task<NavigateResult> EditorOpenAtSymbolAsync(
        string symbolPath, string? kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbolPath)) throw new VsmcpException(ErrorCodes.NotFound, "symbolPath is required.");
        var match = await CodeFindSymbolAsync(symbolPath, kind, 5, cancellationToken).ConfigureAwait(false);
        var first = match.Matches.FirstOrDefault();
        if (first?.Location is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbolPath}");

        await EditorOpenAsync(first.Location.File, first.Location.StartLine, first.Location.StartColumn, cancellationToken).ConfigureAwait(false);
        return new NavigateResult
        {
            Opened = true,
            Line = first.Location.StartLine,
            Column = first.Location.StartColumn,
        };
    }

    public async Task<BreakpointInfo> BreakpointSetOnEntryAsync(
        string className, string methodName, string? condition, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(methodName)) throw new VsmcpException(ErrorCodes.NotFound, "methodName is required.");

        var match = await CodeFindSymbolAsync(className + "." + methodName, "method", 5, cancellationToken).ConfigureAwait(false);
        var first = match.Matches.FirstOrDefault();
        if (first?.Location is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Method not found: {className}.{methodName}");

        var bpOpts = new BreakpointSetOptions
        {
            Kind = BreakpointKind.Line,
            File = first.Location.File,
            Line = first.Location.StartLine,
        };
        if (!string.IsNullOrEmpty(condition))
        {
            bpOpts.ConditionKind = BreakpointConditionKind.WhenTrue;
            bpOpts.ConditionExpression = condition;
        }
        return await BreakpointSetAsync(bpOpts, cancellationToken).ConfigureAwait(false);
    }
}
