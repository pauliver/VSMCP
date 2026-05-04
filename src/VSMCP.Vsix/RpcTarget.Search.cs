using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M13: Search Operations --------

    public async Task<TextSearchResult> SearchTextAsync(
        string pattern, string? filePattern, string? projectId,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new VsmcpException(ErrorCodes.NotFound, "pattern is required.");
        if (maxResults <= 0) maxResults = 500;

        var result = new TextSearchResult();
        Regex rx;
        try { rx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
        catch (ArgumentException ex) { throw new VsmcpException(ErrorCodes.NotFound, $"Invalid regex: {ex.Message}"); }

        var files = await FileListAsync(projectId, null, filePattern, kinds ?? new[] { "file" }, 50_000, cancellationToken);

        foreach (var f in files.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] lines;
            try { lines = File.ReadAllLines(f.Path); }
            catch { continue; }

            for (int i = 0; i < lines.Length; i++)
            {
                var m = rx.Match(lines[i]);
                if (!m.Success) continue;

                var match = new TextMatch
                {
                    File = f.Path,
                    Line = i + 1,
                    Column = m.Index + 1,
                    LineText = lines[i],
                };
                for (int b = Math.Max(0, i - 2); b < i; b++) match.ContextBefore.Add(lines[b]);
                for (int a = i + 1; a < Math.Min(lines.Length, i + 3); a++) match.ContextAfter.Add(lines[a]);

                result.Matches.Add(match);
                if (result.Matches.Count >= maxResults)
                {
                    result.Truncated = true;
                    result.Total = result.Matches.Count;
                    return result;
                }
            }
        }

        result.Total = result.Matches.Count;
        return result;
    }

    public async Task<SymbolSearchResultContainer> SearchSymbolAsync(
        string namePattern, IReadOnlyList<string>? kinds, string? container,
        int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(namePattern))
            throw new VsmcpException(ErrorCodes.NotFound, "namePattern is required.");
        if (maxResults <= 0) maxResults = 500;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var rx = GlobToRegex(namePattern);
        var result = new SymbolSearchResultContainer();

        foreach (var project in ws.CurrentSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hits = await SymbolFinder.FindDeclarationsAsync(
                project, namePattern.Replace("*", "").Replace("?", ""), ignoreCase: true, cancellationToken).ConfigureAwait(false);
            foreach (var sym in hits)
            {
                if (!rx.IsMatch(sym.Name)) continue;
                if (kinds is not null && !kinds.Contains(sym.Kind.ToString().ToLowerInvariant())) continue;
                if (container is not null && !string.Equals(
                    sym.ContainingSymbol?.ToDisplayString(), container, StringComparison.OrdinalIgnoreCase)) continue;

                result.Symbols.Add(new SymbolSearchResult
                {
                    Name = sym.Name,
                    Kind = sym.Kind.ToString().ToLowerInvariant(),
                    Location = GetCodeSpan(sym),
                    Container = sym.ContainingSymbol?.ToDisplayString(),
                    Signature = sym.ToDisplayString(),
                });
                if (result.Symbols.Count >= maxResults)
                {
                    result.Truncated = true;
                    result.Total = result.Symbols.Count;
                    return result;
                }
            }
        }

        result.Total = result.Symbols.Count;
        return result;
    }

    public async Task<ClassSearchResultContainer> SearchClassesAsync(
        string? namePattern, string? baseType, string? @interface,
        int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0) maxResults = 500;
        var ws = await GetWorkspaceAsync(cancellationToken);
        var rx = string.IsNullOrEmpty(namePattern) ? null : GlobToRegex(namePattern!);
        var result = new ClassSearchResultContainer();

        foreach (var project in ws.CurrentSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var t in compilation.GlobalNamespace.GetAllTypesInternal(cancellationToken))
            {
                if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Struct && t.TypeKind != TypeKind.Interface) continue;
                if (rx is not null && !rx.IsMatch(t.Name)) continue;

                if (baseType is not null)
                {
                    var matches = false;
                    var b = t.BaseType;
                    while (b is not null)
                    {
                        if (string.Equals(b.Name, baseType, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(b.ToDisplayString(), baseType, StringComparison.OrdinalIgnoreCase))
                        { matches = true; break; }
                        b = b.BaseType;
                    }
                    if (!matches) continue;
                }
                if (@interface is not null
                    && !t.AllInterfaces.Any(i =>
                        string.Equals(i.Name, @interface, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(i.ToDisplayString(), @interface, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Classes.Add(new ClassSearchResult
                {
                    Name = t.ToDisplayString(),
                    Location = GetCodeSpan(t),
                    Base = t.BaseType?.ToDisplayString(),
                    Interfaces = t.Interfaces.Select(i => i.ToDisplayString()).ToList(),
                });
                if (result.Classes.Count >= maxResults)
                {
                    result.Total = result.Classes.Count;
                    return result;
                }
            }
        }

        result.Total = result.Classes.Count;
        return result;
    }

    public async Task<MemberSearchResultContainer> SearchMembersAsync(
        string namePattern, IReadOnlyList<string>? kinds, string? container,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(namePattern))
            throw new VsmcpException(ErrorCodes.NotFound, "namePattern is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var rx = GlobToRegex(namePattern);
        var result = new MemberSearchResultContainer();

        foreach (var project in ws.CurrentSolution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var t in compilation.GlobalNamespace.GetAllTypesInternal(cancellationToken))
            {
                if (container is not null && !string.Equals(t.ToDisplayString(), container, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var m in t.GetMembers())
                {
                    if (m.IsImplicitlyDeclared) continue;
                    if (!rx.IsMatch(m.Name)) continue;
                    var kind = m.Kind.ToString().ToLowerInvariant();
                    if (kinds is not null && !kinds.Contains(kind)) continue;

                    result.Members.Add(new MemberSearchResult
                    {
                        Name = m.Name,
                        Kind = kind,
                        Signature = m.ToDisplayString(),
                        Location = GetCodeSpan(m),
                        Container = t.ToDisplayString(),
                    });
                }
            }
        }

        result.Total = result.Members.Count;
        return result;
    }

    public async Task<UsageResult> SearchFindUsagesAsync(
        string file, CodePosition position, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var sym = await SymbolFinder.FindSymbolAtPositionAsync(doc, pos, cancellationToken).ConfigureAwait(false);
        if (sym is null) return new UsageResult();

        var result = new UsageResult();
        var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
        foreach (var r in refs)
        {
            foreach (var loc in r.Locations)
            {
                var span = loc.Location.GetLineSpan();
                result.Usages.Add(new Usage
                {
                    File = span.Path ?? "",
                    Line = span.StartLinePosition.Line + 1,
                    Column = span.StartLinePosition.Character + 1,
                    Type = "reference",
                });
            }
            foreach (var d in r.Definition.Locations.Where(l => l.IsInSource))
            {
                var span = d.GetLineSpan();
                result.Usages.Add(new Usage
                {
                    File = span.Path ?? "",
                    Line = span.StartLinePosition.Line + 1,
                    Column = span.StartLinePosition.Character + 1,
                    Type = "definition",
                });
            }
        }
        result.Total = result.Usages.Count;
        return result;
    }
}

internal static class NamespaceTypeWalker
{
    public static IEnumerable<INamedTypeSymbol> GetAllTypesInternal(this INamespaceSymbol ns, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var t in ns.GetTypeMembers())
        {
            yield return t;
            foreach (var nested in GetNestedTypes(t, ct)) yield return nested;
        }
        foreach (var sub in ns.GetNamespaceMembers())
            foreach (var t in sub.GetAllTypesInternal(ct))
                yield return t;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol t, CancellationToken ct)
    {
        foreach (var nt in t.GetTypeMembers())
        {
            yield return nt;
            foreach (var deeper in GetNestedTypes(nt, ct)) yield return deeper;
        }
    }
}
