using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// Auto-discover include roots. Walks up from the source file looking for ancestor directories
    /// (with priority for the open Folder/solution root). Caller-supplied includes are kept verbatim
    /// and prepended.
    /// </summary>
    private async Task<string[]> ResolveCppIncludesAsync(string file, string[]? caller, CancellationToken ct)
    {
        var resolved = new List<string>();
        if (caller is not null) resolved.AddRange(caller.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Workspace root from DTE (when in Open Folder mode `solution.FullName` is the folder path).
        try
        {
            await _jtf.SwitchToMainThreadAsync(ct);
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is DTE2 dte
                && dte.Solution is { IsOpen: true } sln
                && !string.IsNullOrEmpty(sln.FullName))
            {
                var slnPath = sln.FullName;
                var rootDir = Directory.Exists(slnPath) ? slnPath : Path.GetDirectoryName(slnPath);
                if (!string.IsNullOrEmpty(rootDir) && Directory.Exists(rootDir))
                    resolved.Add(rootDir!);
            }
        }
        catch { /* best-effort */ }

        // Source file's directory + a few ancestors as fallback.
        var fileDir = Path.GetDirectoryName(file);
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(fileDir); i++)
        {
            if (!resolved.Contains(fileDir!, StringComparer.OrdinalIgnoreCase))
                resolved.Add(fileDir!);
            var parent = Path.GetDirectoryName(fileDir);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, fileDir, StringComparison.OrdinalIgnoreCase)) break;
            fileDir = parent;
        }

        return resolved.ToArray();
    }

    public async Task<CppDiagnosticsResult> CppDiagnosticsAsync(string file, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await EnsurePchPushedAsync(file, cancellationToken).ConfigureAwait(false);
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        var result = await proxy.DiagnosticsAsync(file, includes, extraDefines, cancellationToken).ConfigureAwait(false);
        if (Follow.Enabled)
            await Follow.TouchAsync(file, 1, 1, isEdit: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CppLocationListResult> CppFindReferencesSemAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await EnsurePchPushedAsync(file, cancellationToken).ConfigureAwait(false);
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        var result = await proxy.FindReferencesAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
        if (Follow.Enabled)
            await Follow.TouchAsync(file, line, column, isEdit: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CppQuickInfoResult> CppQuickInfoAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await EnsurePchPushedAsync(file, cancellationToken).ConfigureAwait(false);
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        var result = await proxy.QuickInfoAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
        if (Follow.Enabled)
            await Follow.TouchAsync(file, line, column, isEdit: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CppLocationResult> CppGotoDefinitionAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await EnsurePchPushedAsync(file, cancellationToken).ConfigureAwait(false);
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        var result = await proxy.GotoDefinitionAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
        // Follow the definition target if we got one; else fall back to the cursor location.
        if (Follow.Enabled)
        {
            var loc = result.Location;
            var f = !string.IsNullOrEmpty(loc?.File) ? loc!.File : file;
            var ln = loc?.Line ?? line;
            var col = loc?.Column ?? column;
            await Follow.TouchAsync(f, ln, col, isEdit: false, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async Task CppInvalidateAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        await proxy.InvalidateAsync(file, cancellationToken).ConfigureAwait(false);
    }

    public async Task CppSetUnsavedBufferAsync(string file, string? content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        await proxy.SetUnsavedBufferAsync(file, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task CppSetPchHeaderAsync(string file, string? pchHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        await proxy.SetPchHeaderAsync(file, pchHeader, cancellationToken).ConfigureAwait(false);
    }

    private static readonly System.Collections.Generic.HashSet<string> s_pchPushed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_pchPushedLock = new();

    /// <summary>
    /// Auto-detect the PCH header for a source file (via .vcxproj walk) and push it to the
    /// analyzer once per file path. No-op if no PCH is configured. Best-effort; failures
    /// are swallowed so the original semantic call isn't blocked.
    /// </summary>
    private async Task EnsurePchPushedAsync(string file, CancellationToken ct)
    {
        var full = Path.GetFullPath(file);
        lock (s_pchPushedLock) { if (!s_pchPushed.Add(full)) return; }
        try
        {
            var pch = AutoDetectPchHeader(full);
            if (string.IsNullOrEmpty(pch)) return;
            await CppSetPchHeaderAsync(full, pch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { VsmcpLog.Debug("cpp.pch", $"EnsurePchPushedAsync({full})", ex); }
    }

    /// <summary>
    /// Walk up from a source file looking for a sibling .vcxproj that declares a
    /// &lt;PrecompiledHeaderFile&gt; element. Returns the absolute path of the PCH header
    /// (resolved relative to the .vcxproj directory), or null if no PCH is configured.
    /// </summary>
    private static string? AutoDetectPchHeader(string sourceFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(sourceFile);
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string[] vcxprojs;
                try { vcxprojs = Directory.GetFiles(dir!, "*.vcxproj"); } catch { vcxprojs = Array.Empty<string>(); }
                if (vcxprojs.Length > 0)
                {
                    foreach (var vcx in vcxprojs)
                    {
                        try
                        {
                            var doc = System.Xml.Linq.XDocument.Load(vcx);
                            var ns = doc.Root?.Name.NamespaceName ?? "";
                            var pch = doc.Descendants(System.Xml.Linq.XName.Get("PrecompiledHeaderFile", ns))
                                .Select(e => e.Value)
                                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                            if (string.IsNullOrEmpty(pch)) continue;
                            // Resolve relative to the .vcxproj directory.
                            var vcxDir = Path.GetDirectoryName(vcx)!;
                            var resolved = Path.IsPathRooted(pch) ? pch : Path.GetFullPath(Path.Combine(vcxDir, pch!));
                            if (File.Exists(resolved)) return resolved;
                            // Also try the conventional <vcxDir>/<pch> match.
                            var conventional = Path.Combine(vcxDir, Path.GetFileName(pch!));
                            if (File.Exists(conventional)) return conventional;
                        }
                        catch { /* skip malformed .vcxproj */ }
                    }
                }
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) break;
                dir = parent;
            }
        }
        catch { }
        return null;
    }

    private static int s_unsavedBufferHooked;

    /// <summary>
    /// Wire DocumentSavedExternal to clear the analyzer's unsaved-buffer override for that
    /// file (analyzer reverts to disk content). Idempotent; runs once per process.
    /// </summary>
    internal void TryEnableUnsavedBufferAutoClear(WorkspaceWatcher watcher)
    {
        if (System.Threading.Interlocked.Exchange(ref s_unsavedBufferHooked, 1) != 0) return;
        watcher.DocumentSavedExternal += async path =>
        {
            // Only push the clear if the analyzer is already up. Don't spawn it just to
            // forward a save event for a non-cpp file, or for a project that never used
            // libclang in the first place.
            var probe = CppAnalyzerHost.Probe();
            if (!probe.spawnAttempted) return;
            try { await CppSetUnsavedBufferAsync(path, null, default).ConfigureAwait(false); }
            catch { /* analyzer may have died — best-effort */ }
        };
    }

    /// <summary>
    /// Sync the analyzer's unsaved-buffer table for a single file with the live VS editor.
    /// If the doc is open AND dirty, push the in-memory text. Otherwise clear any override.
    /// Best-effort; failures are swallowed so callers don't need to wrap.
    /// </summary>
    internal async Task SyncDirtyBufferToAnalyzerAsync(string file, CancellationToken ct)
    {
        try
        {
            await _jtf.SwitchToMainThreadAsync(ct);
            string? content = null;
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is DTE2 dte
                && dte.Documents is not null)
            {
                foreach (EnvDTE.Document doc in dte.Documents)
                {
                    if (doc is null) continue;
                    if (!string.Equals(doc.FullName, file, StringComparison.OrdinalIgnoreCase)) continue;
                    if (doc.Saved) break; // not dirty — clear below
                    if (doc.Object("TextDocument") is EnvDTE.TextDocument td)
                    {
                        var ep = td.StartPoint.CreateEditPoint();
                        content = ep.GetText(td.EndPoint);
                    }
                    break;
                }
            }
            await CppSetUnsavedBufferAsync(file, content, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { VsmcpLog.Debug("cpp.dirty-buffer", $"SyncDirtyBufferToAnalyzerAsync({file})", ex); }
    }

    public async Task<CppLocationListResult> CppFindReferencesSolutionAsync(string file, int line, int column, int maxFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (maxFiles <= 0) maxFiles = 200;

        // Enumerate all C++ files in the solution to walk for refs. Ask for one more than the
        // cap so a truncated walk is detectable (Truncated flag on the result).
        var allFiles = await FileListAsync(null, null, "*.{h,hpp,hxx,hh,c,cpp,cc,cxx}",
            new[] { "file" }, maxFiles + 1, cancellationToken).ConfigureAwait(false);
        var candidates = allFiles.Files
            .Select(f => f.Path)
            .Where(p => !string.Equals(p, System.IO.Path.GetFullPath(file), System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        var others = candidates.Take(maxFiles).ToArray();

        await EnsurePchPushedAsync(file, cancellationToken).ConfigureAwait(false);
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        var result = await proxy.FindReferencesInFilesAsync(file, line, column, others, includes, extraDefines, cancellationToken).ConfigureAwait(false);
        result.Truncated = candidates.Count > others.Length;
        if (Follow.Enabled)
            await Follow.TouchAsync(file, line, column, isEdit: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CppLocationListResult> CppRenameAsync(string file, int line, int column, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(newName)) throw new VsmcpException(ErrorCodes.NotFound, "newName is required.");

        // Single-TU rename: find references in the seed file via the analyzer.
        var refs = await CppFindReferencesSemAsync(file, line, column, null, null, cancellationToken).ConfigureAwait(false);
        if (refs.Locations.Count == 0)
            return refs;

        // Group by file (here always one file in v1 since CppFindReferencesSem is single-TU).
        // Replace each reference: read the line, splice newName at (col, col + len(spelling)).
        var oldName = refs.Spelling ?? "";
        if (string.IsNullOrEmpty(oldName))
            throw new VsmcpException(ErrorCodes.WrongState, "Could not determine the old name from the cursor.");

        // Re-verify each splice site against current content so a stale parse can't corrupt the
        // file — mismatched sites are skipped, not spliced.
        string[] currentLines;
        try
        {
            currentLines = (await FileReadAsync(file, null, cancellationToken).ConfigureAwait(false)).Content.Split('\n');
        }
        catch
        {
            currentLines = System.Array.Empty<string>();
        }

        // Sort locations bottom-up so earlier edits don't shift later ones.
        var ordered = refs.Locations
            .OrderByDescending(l => l.Line)
            .ThenByDescending(l => l.Column)
            .ToList();

        foreach (var loc in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(loc.File)) continue;
            if (!SpliceSiteMatches(currentLines, loc.Line, loc.Column, oldName)) continue;
            // Splice: replace the [column, column+oldName.Length) range with newName.
            await FileReplaceRangeAsync(loc.File, new FileRange
            {
                StartLine = loc.Line,
                StartColumn = loc.Column,
                EndLine = loc.Line,
                EndColumn = loc.Column + oldName.Length,
            }, newName, cancellationToken).ConfigureAwait(false);
        }

        // Invalidate so subsequent semantic queries reparse.
        try { await CppInvalidateAsync(file, cancellationToken).ConfigureAwait(false); } catch { }

        // Return refs (their locations will be slightly stale post-edit but useful as audit trail).
        return refs;
    }
}
