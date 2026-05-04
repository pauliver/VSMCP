using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M16: Navigation Context --------

    public async Task<NavigateResult> EditorNavigateToAsync(
        string file, int? line, int? column, bool openInEditor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var l = Math.Max(1, line ?? 1);
        var c = Math.Max(1, column ?? 1);

        if (openInEditor)
            await EditorOpenAsync(file, l, c, cancellationToken).ConfigureAwait(false);

        return new NavigateResult { Opened = openInEditor, Line = l, Column = c };
    }

    public async Task<SnippetResult> EditorSnippetAsync(
        string file, int line, int contextBefore, int contextAfter, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        await Task.Yield();
        if (line < 1) line = 1;
        if (contextBefore < 0) contextBefore = 0;
        if (contextAfter < 0) contextAfter = 0;

        var lines = File.ReadAllLines(file);
        if (lines.Length == 0)
            return new SnippetResult { Line = new SnippetLine { Number = line, Text = "" } };

        int idx = Math.Min(line - 1, lines.Length - 1);
        var result = new SnippetResult
        {
            Line = new SnippetLine { Number = idx + 1, Text = lines[idx] },
        };
        for (int b = Math.Max(0, idx - contextBefore); b < idx; b++) result.Before.Add(lines[b]);
        for (int a = idx + 1; a < Math.Min(lines.Length, idx + 1 + contextAfter); a++) result.After.Add(lines[a]);
        return result;
    }

    public async Task<RegionResult> EditorExpandRegionAsync(
        string file, int line, CancellationToken cancellationToken = default)
        => await ToggleRegionAsync(file, line, expand: true, cancellationToken).ConfigureAwait(false);

    public async Task<RegionResult> EditorCollapseRegionAsync(
        string file, int line, CancellationToken cancellationToken = default)
        => await ToggleRegionAsync(file, line, expand: false, cancellationToken).ConfigureAwait(false);

    private async Task<RegionResult> ToggleRegionAsync(string file, int line, bool expand, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await _jtf.SwitchToMainThreadAsync(ct);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        await EditorOpenAsync(file, line, 1, ct).ConfigureAwait(false);

        try
        {
            // Edit.ToggleOutliningExpansion toggles at the cursor; without a programmatic
            // way to query state, we just toggle once. Best-effort.
            dte.ExecuteCommand("Edit.ToggleOutliningExpansion");
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.Unsupported, $"Region toggle failed (file may not have outlining): {ex.Message}");
        }

        // Determine the region range from #region/#endregion if present.
        var range = FindRegionRange(file, line);
        return new RegionResult
        {
            Expanded = expand,
            Collapsed = !expand,
            Range = range ?? new RegionRange { StartLine = line, EndLine = line },
        };
    }

    private static RegionRange? FindRegionRange(string file, int targetLine)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            // Look backwards for #region, then forwards for #endregion.
            int start = -1;
            for (int i = Math.Min(targetLine - 1, lines.Length - 1); i >= 0; i--)
            {
                if (lines[i].TrimStart().StartsWith("#region", StringComparison.Ordinal)) { start = i; break; }
                if (lines[i].TrimStart().StartsWith("#endregion", StringComparison.Ordinal)) break; // crossed boundary
            }
            if (start < 0) return null;
            int end = -1;
            int depth = 1;
            for (int i = start + 1; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("#region", StringComparison.Ordinal)) depth++;
                else if (t.StartsWith("#endregion", StringComparison.Ordinal))
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }
            if (end < 0) return null;
            return new RegionRange { StartLine = start + 1, EndLine = end + 1 };
        }
        catch { return null; }
    }

    public async Task<IncludeNavigationResult> EditorNavigateToIncludeAsync(
        string file, string includeName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(includeName)) throw new VsmcpException(ErrorCodes.NotFound, "includeName is required.");

        var deps = await FileDependenciesAsync(file, cancellationToken).ConfigureAwait(false);
        var hit = deps.Includes.FirstOrDefault(d =>
            string.Equals(d.File, includeName, StringComparison.OrdinalIgnoreCase)
            || d.File.EndsWith("/" + includeName, StringComparison.OrdinalIgnoreCase)
            || d.File.EndsWith("\\" + includeName, StringComparison.OrdinalIgnoreCase));
        if (hit is null) throw new VsmcpException(ErrorCodes.NotFound, $"Include '{includeName}' not found in {file}.");

        var dir = Path.GetDirectoryName(file)!;
        var resolvedPath = Path.IsPathRooted(hit.File) ? hit.File : Path.Combine(dir, hit.File);
        if (!File.Exists(resolvedPath))
            throw new VsmcpException(ErrorCodes.NotFound, $"Header file not found on disk: {resolvedPath}");

        await EditorOpenAsync(resolvedPath, 1, 1, cancellationToken).ConfigureAwait(false);

        return new IncludeNavigationResult
        {
            Found = new IncludeNavigationResultFound { File = resolvedPath, Line = 1 },
            Navigation = new IncludeNavigationNavigation { FromLine = hit.Line, ToLine = 1 },
        };
    }
}
