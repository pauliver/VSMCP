using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    private static readonly HashSet<string> ClassLikeKinds = new(StringComparer.OrdinalIgnoreCase)
    { "class", "struct", "union", "enum" };

    public async Task<CppClassesResult> CppClassesAsync(
        string? namePattern, IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0) maxResults = 1000;
        var allowed = (kinds is null || kinds.Count == 0)
            ? ClassLikeKinds
            : new HashSet<string>(kinds, StringComparer.OrdinalIgnoreCase);
        var nameRx = string.IsNullOrEmpty(namePattern) ? null : GlobToRegex(namePattern!);

        var result = new CppClassesResult();
        var hits = await EnumerateCppDeclsAsync(maxResults, allowed, nameRx, cancellationToken).ConfigureAwait(false);
        foreach (var (decl, file) in hits)
        {
            result.Classes.Add(new CppFileDecl
            {
                Kind = decl.Kind,
                Name = decl.Name,
                Container = decl.Container,
                File = file,
                Line = decl.Line,
                Signature = decl.Signature,
            });
            if (result.Classes.Count >= maxResults) { result.Truncated = true; break; }
        }
        result.Total = result.Classes.Count;
        return result;
    }

    public async Task<CppFindSymbolResult> CppFindSymbolAsync(
        string name, string? kind, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name)) throw new VsmcpException(ErrorCodes.NotFound, "name is required.");
        if (maxResults <= 0) maxResults = 100;

        var allowed = string.IsNullOrEmpty(kind)
            ? null
            : new HashSet<string>(new[] { kind! }, StringComparer.OrdinalIgnoreCase);
        var nameRx = GlobToRegex(name);

        var result = new CppFindSymbolResult();
        var hits = await EnumerateCppDeclsAsync(maxResults, allowed, nameRx, cancellationToken).ConfigureAwait(false);
        foreach (var (decl, file) in hits)
        {
            result.Matches.Add(new CppFileDecl
            {
                Kind = decl.Kind,
                Name = decl.Name,
                Container = decl.Container,
                File = file,
                Line = decl.Line,
                Signature = decl.Signature,
            });
            if (result.Matches.Count >= maxResults) { result.Truncated = true; break; }
        }
        result.Total = result.Matches.Count;
        return result;
    }

    private async Task<List<(CppDecl decl, string file)>> EnumerateCppDeclsAsync(
        int maxResults, HashSet<string>? allowedKinds, Regex? nameRx, CancellationToken ct)
    {
        var hits = new List<(CppDecl decl, string file)>(capacity: Math.Min(maxResults, 256));

        // FileListAsync glob covers .h / .hpp / .hxx / .hh / .c / .cpp / .cc / .cxx.
        var files = await FileListAsync(null, null, "*.{h,hpp,hxx,hh,c,cpp,cc,cxx}",
            new[] { "file" }, 50_000, ct).ConfigureAwait(false);

        foreach (var f in files.Files)
        {
            ct.ThrowIfCancellationRequested();
            CppOutlineResult outline;
            try { outline = CppOutlineParser.Parse(f.Path); }
            catch { continue; }

            foreach (var decl in outline.Declarations)
            {
                if (allowedKinds is not null && !allowedKinds.Contains(decl.Kind)) continue;
                if (nameRx is not null && !nameRx.IsMatch(decl.Name)) continue;
                hits.Add((decl, f.Path));
                if (hits.Count >= maxResults) return hits;
            }
        }

        return hits;
    }
}
