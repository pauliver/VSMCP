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
}
