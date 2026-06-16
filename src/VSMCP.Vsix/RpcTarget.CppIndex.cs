using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // Process-wide so it survives per-connection RpcTarget recreation, like the C++ sidecar.
    private static readonly CppSymbolIndex s_cppIndex = new();

    /// <summary>
    /// Solution-wide syntactic symbol lookup backed by a persistent index (#141). Builds the
    /// index lazily on first use (parsing every solution C/C++ file once) so subsequent queries
    /// don't re-parse the tree. Unbounded by file count; results capped by maxResults.
    /// </summary>
    public async Task<CppIndexResult> CppIndexFindAsync(string name, string? kind, int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0) maxResults = 200;
        if (maxResults > 5000) maxResults = 5000;
        if (s_cppIndex.FileCount == 0)
            await BuildCppIndexAsync(cancellationToken).ConfigureAwait(false);

        var entries = s_cppIndex.Find(name ?? "", string.IsNullOrWhiteSpace(kind) ? null : kind, maxResults, out var truncated);
        return new CppIndexResult
        {
            Entries = new List<CppIndexEntry>(entries),
            TotalIndexed = s_cppIndex.SymbolCount,
            Truncated = truncated,
        };
    }

    /// <summary>Force a full rebuild of the C++ symbol index and return its size.</summary>
    public async Task<CppIndexStatsResult> CppIndexRebuildAsync(CancellationToken cancellationToken = default)
    {
        await BuildCppIndexAsync(cancellationToken).ConfigureAwait(false);
        return new CppIndexStatsResult { Files = s_cppIndex.FileCount, Symbols = s_cppIndex.SymbolCount };
    }

    private async Task BuildCppIndexAsync(CancellationToken ct)
    {
        // Reuse the same enumeration the syntactic search uses: project files (DTE) plus a walk
        // of the repo root for loose headers. WalkCppFilesUnder / AscendToRepoRoot are private
        // statics on this same RpcTarget partial class (RpcTarget.CppSearch.cs).
        var listed = await FileListAsync(null, null, "*.{h,hpp,hxx,hh,c,cpp,cc,cxx}",
            new[] { "file" }, 50_000, ct).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var f in listed.Files) if (seen.Add(f.Path)) paths.Add(f.Path);

        await _jtf.SwitchToMainThreadAsync(ct);
        try
        {
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte
                && dte.Solution is { IsOpen: true } sln && !string.IsNullOrEmpty(sln.FullName))
            {
                var root = sln.FullName;
                if (!Directory.Exists(root)) root = Path.GetDirectoryName(root);
                root = AscendToRepoRoot(root);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    foreach (var p in WalkCppFilesUnder(root!))
                        if (seen.Add(p)) paths.Add(p);
            }
        }
        catch { /* best-effort; project files alone are still indexed */ }

        s_cppIndex.Clear();
        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outline = CppOutlineParser.Parse(p);
                s_cppIndex.AddOrReplaceFile(p, outline.Declarations);
            }
            catch (Exception ex) { VsmcpLog.Debug("cpp.index", $"parse failed: {p}", ex); }
        }
    }
}
