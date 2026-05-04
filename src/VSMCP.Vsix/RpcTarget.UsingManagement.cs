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
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M18: Using / Include directive management --------

    public async Task<AddUsingResult> EditAddUsingAsync(
        string file, string namespaceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(namespaceName)) throw new VsmcpException(ErrorCodes.NotFound, "namespaceName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null) throw new VsmcpException(ErrorCodes.NotFound, "Not a C# compilation unit.");

        if (root.Usings.Any(u => string.Equals(u.Name?.ToString(), namespaceName, StringComparison.Ordinal)))
            return new AddUsingResult { Added = false, AlreadyPresent = true };

        var usingDir = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        var newUsings = root.Usings
            .Add(usingDir)
            .OrderBy(u => u.Name?.ToString().StartsWith("System", StringComparison.Ordinal) == true ? 0 : 1)
            .ThenBy(u => u.Name?.ToString(), StringComparer.Ordinal);

        var newRoot = root.WithUsings(SyntaxFactory.List(newUsings));
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        RoslynEditor.ApplyDocumentChange(ws, doc, newRoot);

        var insertedLine = newRoot.Usings
            .First(u => string.Equals(u.Name?.ToString(), namespaceName, StringComparison.Ordinal))
            .GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new AddUsingResult { Added = true, AlreadyPresent = false, InsertedAtLine = insertedLine };
    }

    public async Task<RemoveUsingResult> EditRemoveUsingAsync(
        string file, string namespaceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(namespaceName)) throw new VsmcpException(ErrorCodes.NotFound, "namespaceName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null) throw new VsmcpException(ErrorCodes.NotFound, "Not a C# compilation unit.");

        var target = root.Usings.FirstOrDefault(u => string.Equals(u.Name?.ToString(), namespaceName, StringComparison.Ordinal));
        if (target is null) return new RemoveUsingResult { Removed = false, WasPresent = false };

        var newRoot = root.WithUsings(SyntaxFactory.List(root.Usings.Where(u => u != target)));
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        RoslynEditor.ApplyDocumentChange(ws, doc, newRoot);
        return new RemoveUsingResult { Removed = true, WasPresent = true };
    }

    public async Task<UsingSuggestionsResult> CodeSuggestUsingsAsync(
        string file, IReadOnlyList<string>? symbolNames, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");

        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var result = new UsingSuggestionsResult();
        if (sm is null) return result;

        // Collect unresolved names from CS0246 / CS0103 diagnostics.
        var diagNames = sm.GetDiagnostics(cancellationToken: cancellationToken)
            .Where(d => d.Id == "CS0246" || d.Id == "CS0103")
            .Select(d =>
            {
                var msg = d.GetMessage();
                var m = Regex.Match(msg, @"'([^']+)'");
                return m.Success ? m.Groups[1].Value : null;
            })
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();

        var allNames = (symbolNames ?? new List<string>()).Concat(diagNames!).Distinct().ToList();

        foreach (var name in allNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(name)) continue;
            var declarations = await SymbolFinder.FindDeclarationsAsync(
                doc.Project, name, ignoreCase: false, cancellationToken).ConfigureAwait(false);
            foreach (var sym in declarations.Take(3))
            {
                var ns = sym.ContainingNamespace?.ToDisplayString();
                if (string.IsNullOrEmpty(ns) || ns == "<global namespace>") continue;
                result.Suggestions.Add(new UsingSuggestion
                {
                    SymbolName = name!,
                    Namespace = ns!,
                    Confidence = sym.Kind == SymbolKind.NamedType ? 0.9 : 0.5,
                });
            }
        }
        return result;
    }

    public async Task<AddIncludeResult> EditAddIncludeAsync(
        string file, string headerPath, bool isSystem, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(headerPath)) throw new VsmcpException(ErrorCodes.NotFound, "headerPath is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var lines = File.ReadAllLines(file).ToList();
        var directive = isSystem ? $"#include <{headerPath}>" : $"#include \"{headerPath}\"";

        // Already present?
        var existRx = new Regex(
            isSystem
                ? $@"^\s*#\s*include\s*<\s*{Regex.Escape(headerPath)}\s*>"
                : $@"^\s*#\s*include\s*""\s*{Regex.Escape(headerPath)}\s*""");
        if (lines.Any(l => existRx.IsMatch(l)))
            return new AddIncludeResult { Added = false, AlreadyPresent = true };

        // Insert after the last existing #include, or after #pragma once / leading comments.
        int insertIdx = 0;
        var anyIncludeRx = new Regex(@"^\s*#\s*include");
        var pragmaOrComment = new Regex(@"^\s*(?:#\s*pragma|//|/\*|\*)");

        for (int i = 0; i < lines.Count; i++)
        {
            if (anyIncludeRx.IsMatch(lines[i])) insertIdx = i + 1;
            else if (insertIdx == 0 && pragmaOrComment.IsMatch(lines[i])) insertIdx = i + 1;
            else if (insertIdx > 0) break;
        }

        lines.Insert(insertIdx, directive);
        File.WriteAllText(file, string.Join("\n", lines));

        return new AddIncludeResult { Added = true, AlreadyPresent = false, InsertedAtLine = insertIdx + 1 };
    }
}
