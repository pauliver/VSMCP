using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<FileReadResult> FileReadAsync(string path, FileRange? range, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        if (Follow.Enabled)
            await Follow.TouchAsync(path, range?.StartLine, range?.StartColumn, isEdit: false, cancellationToken).ConfigureAwait(false);

        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        string content;
        bool openInEditor = buffer is not null;
        bool hasUnsavedChanges = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            if (range is not null)
            {
                var span = VsHelpers.ToSpan(snapshot, range);
                content = snapshot.GetText(span);
            }
            else
            {
                content = snapshot.GetText();
            }

            try
            {
                if (buffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out var doc))
                    hasUnsavedChanges = doc.IsDirty;
            }
            catch { }
        }
        else
        {
            if (!File.Exists(path))
                throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

            var full = File.ReadAllText(path);
            if (range is not null)
            {
                var (start, length) = VsHelpers.ToOffsets(full, range);
                content = full.Substring(start, length);
            }
            else
            {
                content = full;
            }
        }

        return new FileReadResult
        {
            Path = path,
            Content = content,
            OpenInEditor = openInEditor,
            HasUnsavedChanges = hasUnsavedChanges,
        };
    }

    public async Task<FileWriteResult> FileWriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        await EnsureWriteAllowedAsync(path, "file.write", cancellationToken).ConfigureAwait(false);

        content ??= string.Empty;

        if (Follow.Enabled && File.Exists(path))
            await Follow.TouchAsync(path, 1, 1, isEdit: true, cancellationToken).ConfigureAwait(false);

        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        bool wentThroughEditor = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            using var edit = buffer.CreateEdit();
            edit.Replace(new Span(0, snapshot.Length), content);
            edit.Apply();
            wentThroughEditor = true;
        }
        else
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = Encoding.UTF8.GetByteCount(content),
            WentThroughEditor = wentThroughEditor,
        };
    }

    public async Task<FileWriteResult> FileReplaceRangeAsync(string path, FileRange range, string text, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");
        if (range is null)
            throw new VsmcpException(ErrorCodes.NotFound, "Range is required.");

        await EnsureWriteAllowedAsync(path, "file.replace_range", cancellationToken).ConfigureAwait(false);

        text ??= string.Empty;

        if (Follow.Enabled)
            await Follow.TouchAsync(path, range.StartLine, range.StartColumn, isEdit: true, cancellationToken).ConfigureAwait(false);

        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        bool wentThroughEditor = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            var span = VsHelpers.ToSpan(snapshot, range);
            using var edit = buffer.CreateEdit();
            edit.Replace(span, text);
            edit.Apply();
            wentThroughEditor = true;
        }
        else
        {
            if (!File.Exists(path))
                throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

            var full = File.ReadAllText(path);
            var (start, length) = VsHelpers.ToOffsets(full, range);
            var sb = new StringBuilder(full.Length + text.Length);
            sb.Append(full, 0, start);
            sb.Append(text);
            sb.Append(full, start + length, full.Length - (start + length));
            File.WriteAllText(path, sb.ToString());
        }

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = Encoding.UTF8.GetByteCount(text),
            WentThroughEditor = wentThroughEditor,
        };
    }

    private string[]? _writeRootsCache;
    private string? _writeRootsSolution; // solution FullName the cache was computed for

    /// <summary>
    /// Confine a mutating file op to the open solution's repository (audit through-line: data safety).
    /// Fails open only when no root can be determined (no solution/folder), so normal edits are never
    /// blocked spuriously; when a root IS known, a write outside it throws WrongState.
    /// </summary>
    private async Task EnsureWriteAllowedAsync(string path, string operation, CancellationToken ct)
    {
        var roots = await GetWriteRootsAsync(ct).ConfigureAwait(false);
        if (roots.Count > 0)
            WriteScopePolicy.EnsureWithinAnyRoot(roots, path, operation);
    }

    /// <summary>True when <paramref name="path"/> is inside the allowed write roots (or no roots
    /// are determinable). Non-throwing variant for best-effort bulk operations that skip rather
    /// than abort.</summary>
    private async Task<bool> IsWriteAllowedAsync(string path, CancellationToken ct)
    {
        var roots = await GetWriteRootsAsync(ct).ConfigureAwait(false);
        return roots.Count == 0 || WriteScopePolicy.IsWithinAnyRoot(roots, path);
    }

    /// <summary>
    /// Allowed write roots: the solution's enclosing git repo root (else the solution dir), PLUS
    /// each loaded project's repo root (solutions routinely reference projects outside the
    /// solution's own repo), PLUS any user-configured "writeScopeRoots" from
    /// %LOCALAPPDATA%\VSMCP\config.json. Cached per solution — recomputed when the open solution
    /// changes, so a solution switch can neither leak the old scope nor block the new one.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetWriteRootsAsync(CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);

        string slnKey = "";
        EnvDTE80.DTE2? dte = null;
        try
        {
            dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            slnKey = dte?.Solution?.FullName ?? "";
        }
        catch { }

        if (_writeRootsCache is not null
            && string.Equals(_writeRootsSolution, slnKey, StringComparison.OrdinalIgnoreCase))
            return _writeRootsCache;

        var roots = new List<string>();
        void AddRoot(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            var repo = AscendToRepoRoot(dir) ?? dir;
            if (!roots.Contains(repo!, StringComparer.OrdinalIgnoreCase)) roots.Add(repo!);
            if (!roots.Contains(dir!, StringComparer.OrdinalIgnoreCase)) roots.Add(dir!);
        }

        try
        {
            if (dte is not null)
            {
                var slnPath = dte.Solution?.FullName;
                if (!string.IsNullOrEmpty(slnPath))
                    AddRoot(Directory.Exists(slnPath) ? slnPath : Path.GetDirectoryName(slnPath));

                // Projects can live outside the solution's repo (other drives, engine dirs);
                // their files are legitimate workspace documents and must stay writable.
                foreach (var project in VsHelpers.EnumerateProjects(dte.Solution))
                {
                    string? projPath = null;
                    try { projPath = project.FullName; } catch { }
                    if (!string.IsNullOrEmpty(projPath))
                        AddRoot(Path.GetDirectoryName(projPath));
                }
            }
        }
        catch { /* no root determinable -> fail open */ }

        // User-configured extra roots — the escape hatch the WrongState error advertises.
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSMCP", "config.json");
            if (File.Exists(configPath))
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath));
                if (json["writeScopeRoots"] is Newtonsoft.Json.Linq.JArray extra)
                {
                    foreach (var item in extra)
                    {
                        var dir = item?.ToString();
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            try { AddRoot(Path.GetFullPath(dir!)); } catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { VsmcpLog.Debug("write-scope", "failed reading writeScopeRoots from config.json", ex); }

        _writeRootsCache = roots.ToArray();
        _writeRootsSolution = slnKey;
        return _writeRootsCache;
    }

    public async Task EditorOpenAsync(string path, int? line, int? column, CancellationToken cancellationToken = default)
    {
        if (Follow.Enabled)
        {
            await Follow.TouchAsync(path, line, column, isEdit: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        await OpenAndScrollCoreAsync(path, line, column, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure open + scroll, with no follow-mode side effects. Used directly when AutoFocus
    /// is off, and called by <see cref="FollowModeManager"/> when it is on.
    /// </summary>
    internal async Task OpenAndScrollCoreAsync(string path, int? line, int? column, CancellationToken cancellationToken)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");
        if (!File.Exists(path))
            throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var window = dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindPrimary);
        // Activate the window so Edit.GoTo + MoveToLineAndOffset target this document. Without
        // Activate, MoveToLineAndOffset can succeed silently without scrolling the view when
        // the doc just loaded.
        try { window?.Activate(); } catch { }

        if (line is null) return;
        var targetLine = Math.Max(1, line.Value);
        var targetCol = Math.Max(1, column ?? 1);

        // Belt-and-suspenders: MoveToLineAndOffset for the caret + Edit.GoTo for view scroll.
        // The combination reliably scrolls in both .sln-bound and Open Folder workspaces, where
        // the EnvDTE selection-move alone sometimes leaves the view at the top.
        if (window?.Document?.Selection is EnvDTE.TextSelection sel)
        {
            try { sel.MoveToLineAndOffset(targetLine, targetCol); } catch { }
        }
        try { dte.ExecuteCommand("Edit.GoTo", targetLine.ToString()); } catch { }
    }

    public async Task EditorSaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        foreach (EnvDTE.Document doc in dte.Documents)
        {
            if (doc is null) continue;
            if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase))
            {
                doc.Save();
                return;
            }
        }
        throw new VsmcpException(ErrorCodes.NotFound, $"No open document matching '{path}'.");
    }

    public async Task EditorSaveAllAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        dte.Documents.SaveAll();
    }
}
