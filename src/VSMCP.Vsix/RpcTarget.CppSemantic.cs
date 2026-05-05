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
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        return await proxy.DiagnosticsAsync(file, includes, extraDefines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CppLocationListResult> CppFindReferencesSemAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        return await proxy.FindReferencesAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CppQuickInfoResult> CppQuickInfoAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        return await proxy.QuickInfoAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CppLocationResult> CppGotoDefinitionAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        return await proxy.GotoDefinitionAsync(file, line, column, includes, extraDefines, cancellationToken).ConfigureAwait(false);
    }

    public async Task CppInvalidateAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        await proxy.InvalidateAsync(file, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CppLocationListResult> CppFindReferencesSolutionAsync(string file, int line, int column, int maxFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (maxFiles <= 0) maxFiles = 200;

        // Enumerate all C++ files in the solution to walk for refs.
        var allFiles = await FileListAsync(null, null, "*.{h,hpp,hxx,hh,c,cpp,cc,cxx}",
            new[] { "file" }, maxFiles, cancellationToken).ConfigureAwait(false);
        var others = allFiles.Files
            .Select(f => f.Path)
            .Where(p => !string.Equals(p, System.IO.Path.GetFullPath(file), System.StringComparison.OrdinalIgnoreCase))
            .Take(maxFiles)
            .ToArray();

        var includes = await ResolveCppIncludesAsync(file, extraIncludes, cancellationToken).ConfigureAwait(false);
        var proxy = await CppAnalyzerHost.GetProxyAsync(cancellationToken).ConfigureAwait(false);
        return await proxy.FindReferencesInFilesAsync(file, line, column, others, includes, extraDefines, cancellationToken).ConfigureAwait(false);
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

        // Sort locations bottom-up so earlier edits don't shift later ones.
        var ordered = refs.Locations
            .OrderByDescending(l => l.Line)
            .ThenByDescending(l => l.Column)
            .ToList();

        foreach (var loc in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(loc.File)) continue;
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
