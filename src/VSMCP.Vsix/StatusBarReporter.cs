using System;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace VSMCP.Vsix;

/// <summary>
/// Mirrors <see cref="HostActivity"/> onto the VS main status bar so a live
/// "VSMCP: connected (1) · 42 RPCs" readout is visible without opening the
/// tool window. Uses <see cref="IVsStatusbar.SetText"/>; other components can
/// overwrite it, so we reassert every 2s via a dispatcher tick.
/// </summary>
internal sealed class StatusBarReporter : IDisposable
{
    private readonly HostActivity _activity;
    private readonly IVsStatusbar _bar;
    private readonly JoinableTaskFactory _jtf;
    private readonly DispatcherTimer _tick;
    private bool _disposed;

    public StatusBarReporter(HostActivity activity, IVsStatusbar bar, JoinableTaskFactory jtf)
    {
        _activity = activity;
        _bar = bar;
        _jtf = jtf;

        _activity.Changed += OnChanged;

        _tick = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _tick.Tick += (_, __) => Push();
        _tick.Start();

        Push();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        // Changed fires from the pipe host thread; marshal to UI for the status bar.
        _jtf.RunAsync(async () =>
        {
            await _jtf.SwitchToMainThreadAsync();
            Push();
        }).Task.Forget();
    }

    private void Push()
    {
        if (_disposed) return;
        try
        {
            _bar.IsFrozen(out var frozen);
            if (frozen != 0) return;

            var snap = _activity.GetSnapshot();
            string text;
            if (snap.ClientCount > 0)
            {
                var idle = snap.LastActivityUtc == default
                    ? ""
                    : $" · {(int)(DateTime.UtcNow - snap.LastActivityUtc).TotalSeconds}s idle";
                text = $"VSMCP: connected ({snap.ClientCount}) · {snap.RpcCount} RPCs" +
                       (snap.RpcErrorCount > 0 ? $" · {snap.RpcErrorCount} err" : "") +
                       idle;
            }
            else if (snap.RpcCount > 0)
            {
                text = $"VSMCP: idle · {snap.RpcCount} RPCs";
            }
            else
            {
                text = "VSMCP: waiting for client";
            }

            _bar.SetText(text);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        _activity.Changed -= OnChanged;
        try { _bar.SetText(""); } catch { }
    }
}
