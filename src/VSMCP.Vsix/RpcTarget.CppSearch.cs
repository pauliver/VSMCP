using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using VSMCP.Core;
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

        // 1) Enumerate via the loaded projects (DTE).
        var files = await FileListAsync(null, null, "*.{h,hpp,hxx,hh,c,cpp,cc,cxx}",
            new[] { "file" }, 50_000, ct).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.Files) seen.Add(f.Path);

        // 2) Also walk the solution-folder root for C++ files not in any project (loose headers,
        // test fixtures, generated files outside csproj membership). Without this the search is
        // blind to anything VS doesn't enumerate via DTE.
        await _jtf.SwitchToMainThreadAsync(ct);
        string? walkRoot = null;
        try
        {
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte
                && dte.Solution is { IsOpen: true } sln
                && !string.IsNullOrEmpty(sln.FullName))
            {
                var root = sln.FullName;
                if (!Directory.Exists(root)) root = Path.GetDirectoryName(root);
                walkRoot = AscendToRepoRoot(root);
            }
        }
        catch { /* best-effort */ }

        // The disk walk and per-file parse below are pure I/O/CPU — get OFF the UI thread (F5).
        await TaskScheduler.Default;
        try
        {
            if (!string.IsNullOrEmpty(walkRoot) && Directory.Exists(walkRoot))
            {
                foreach (var p in WalkCppFilesUnder(walkRoot!))
                {
                    ct.ThrowIfCancellationRequested();
                    if (seen.Add(p))
                        files.Files.Add(new FileListItem { Path = p, Kind = "file" });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* best-effort */ }

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

    private static readonly HashSet<string> s_cppExts = new(StringComparer.OrdinalIgnoreCase)
    { ".h", ".hpp", ".hxx", ".hh", ".c", ".cpp", ".cc", ".cxx" };

    private static readonly HashSet<string> s_skipDirs = new(StringComparer.OrdinalIgnoreCase)
    { "bin", "obj", ".git", ".vs", "node_modules", "packages", "Build", "out", "Release", "Debug" };

    private static string? AscendToRepoRoot(string? start)
    {
        if (string.IsNullOrEmpty(start)) return start;
        var dir = start;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir!, ".git"))) return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) break;
            dir = parent;
        }
        return start;
    }

    private static IEnumerable<string> WalkCppFilesUnder(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir); } catch { continue; }
            foreach (var f in files)
                if (s_cppExts.Contains(Path.GetExtension(f))) yield return f;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (s_skipDirs.Contains(name)) continue;
                stack.Push(s);
            }
        }
    }
}
