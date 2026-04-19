using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace VSMCP.Vsix;

/// <summary>
/// Brings the host VS main window to the foreground. Used for the explicit <c>vs.focus</c> tool and
/// for auto-focus after every dispatched RPC (teaching mode). Safe to call when VS is already on top.
/// </summary>
internal static class FocusHelper
{
    public static async Task ActivateAsync(VSMCPPackage package, JoinableTaskFactory jtf, CancellationToken ct = default)
    {
        try
        {
            await jtf.SwitchToMainThreadAsync(ct);
            if (await package.GetServiceAsync(typeof(EnvDTE.DTE)) is not DTE2 dte) return;

            var window = dte.MainWindow;
            if (window is null) return;

            try { window.Visible = true; } catch { }
            try { window.Activate(); } catch { }

            // DTE.MainWindow.Activate() is courteous — Win32 foreground-stealing prevention can reduce
            // it to a taskbar flash. Since we're the foreground of our own process already, an explicit
            // SetForegroundWindow on the VS top-level HWND is allowed and reliably raises the window.
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                object raw = window.HWnd;
                hwnd = raw switch
                {
                    IntPtr ip => ip,
                    int i32 => new IntPtr(i32),
                    long i64 => new IntPtr(i64),
                    _ => IntPtr.Zero,
                };
            }
            catch { }
            if (hwnd != IntPtr.Zero)
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                BringWindowToTop(hwnd);
            }
        }
        catch
        {
            // Focus is best-effort; never fail an RPC because the window couldn't be raised.
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
