using System;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<FocusResult> VsFocusAsync(CancellationToken cancellationToken = default)
    {
        await FocusHelper.ActivateAsync(_package, _jtf, cancellationToken).ConfigureAwait(false);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        string? hwnd = null;
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is DTE2 dte && dte.MainWindow is { } win)
        {
            try
            {
                object raw = win.HWnd;
                long value = raw switch
                {
                    IntPtr ip => ip.ToInt64(),
                    int i32 => i32,
                    long i64 => i64,
                    _ => 0,
                };
                hwnd = "0x" + value.ToString("X");
            }
            catch { }
        }

        return new FocusResult { Focused = true, Hwnd = hwnd };
    }

    public Task<AutoFocusResult> VsSetAutoFocusAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        AutoFocusEnabled = enabled;
        return Task.FromResult(new AutoFocusResult { Enabled = AutoFocusEnabled });
    }

    public Task<AutoFocusResult> VsGetAutoFocusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AutoFocusResult { Enabled = AutoFocusEnabled });
}
