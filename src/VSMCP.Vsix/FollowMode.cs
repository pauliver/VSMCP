using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace VSMCP.Vsix;

/// <summary>
/// "Follow mode" / "teaching mode" support: when AutoFocus is enabled, every file the
/// extension reads or edits is opened in the IDE, scrolled to the relevant location,
/// and (after a delay) saved-if-edited and closed — but only if VSMCP is the one that
/// opened it. Reopening a tracked file before its timer fires cancels the pending close.
/// </summary>
internal sealed class FollowModeManager
{
    private sealed class FileState
    {
        public CancellationTokenSource Cts = new();
        public bool NeedsSave;
        public bool WeOpenedIt;
    }

    private readonly Dictionary<string, FileState> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private readonly RpcTarget _rpc;
    private readonly VSMCPPackage _package;
    private readonly JoinableTaskFactory _jtf;
    private readonly TimeSpan _closeDelay;

    public FollowModeManager(RpcTarget rpc, VSMCPPackage package, JoinableTaskFactory jtf, TimeSpan? closeDelay = null)
    {
        _rpc = rpc;
        _package = package;
        _jtf = jtf;
        _closeDelay = closeDelay ?? TimeSpan.FromSeconds(10);
    }

    public bool Enabled => _rpc.AutoFocusEnabled;

    /// <summary>
    /// Open the file, scroll to (line, column), and (re)arm the auto-close timer.
    /// No-op when AutoFocus is disabled. Safe to call from any thread.
    /// </summary>
    public async Task TouchAsync(string path, int? line, int? column, bool isEdit, CancellationToken ct)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) return;

        await _jtf.SwitchToMainThreadAsync(ct);

        var dte = await _package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;

        bool alreadyOpen = dte is not null && IsDocumentOpenCore(dte, path);

        var newCts = new CancellationTokenSource();
        bool weOpenedIt;
        bool needsSave;

        lock (_lock)
        {
            if (_files.TryGetValue(path, out var prev))
            {
                try { prev.Cts.Cancel(); } catch { }
                weOpenedIt = prev.WeOpenedIt;
                needsSave = prev.NeedsSave || isEdit;
            }
            else
            {
                weOpenedIt = !alreadyOpen;
                needsSave = isEdit;
            }

            _files[path] = new FileState
            {
                Cts = newCts,
                NeedsSave = needsSave,
                WeOpenedIt = weOpenedIt,
            };
        }

        await _rpc.OpenAndScrollCoreAsync(path, line, column, ct).ConfigureAwait(true);

        if (!weOpenedIt) return; // user already had it open — never auto-close

        _ = ScheduleCloseAsync(path, newCts.Token);
    }

    /// <summary>Cancel any pending close for a path (e.g. user explicitly saved/closed).</summary>
    public void Forget(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        lock (_lock)
        {
            if (_files.TryGetValue(path, out var s))
            {
                try { s.Cts.Cancel(); } catch { }
                _files.Remove(path);
            }
        }
    }

    private async Task ScheduleCloseAsync(string path, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_closeDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await _jtf.SwitchToMainThreadAsync();

            FileState? s = null;
            lock (_lock)
            {
                if (_files.TryGetValue(path, out var found) && found.Cts.Token == ct)
                {
                    s = found;
                    _files.Remove(path);
                }
            }
            if (s is null) return;

            var dte = await _package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
            if (dte is null) return;

            if (s.NeedsSave) TrySaveCore(dte, path);
            TryCloseCore(dte, path);
        }
        catch
        {
            // best-effort; never throw from background timer
        }
    }

    private static bool IsDocumentOpenCore(DTE2 dte, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            foreach (Document doc in dte.Documents)
            {
                if (doc is null) continue;
                if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static void TrySaveCore(DTE2 dte, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            foreach (Document doc in dte.Documents)
            {
                if (doc is null) continue;
                if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase))
                {
                    try { doc.Save(); } catch { }
                    return;
                }
            }
        }
        catch { }
    }

    private static void TryCloseCore(DTE2 dte, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            foreach (Document doc in dte.Documents)
            {
                if (doc is null) continue;
                if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase))
                {
                    try { doc.Close(vsSaveChanges.vsSaveChangesNo); } catch { }
                    return;
                }
            }
        }
        catch { }
    }
}
