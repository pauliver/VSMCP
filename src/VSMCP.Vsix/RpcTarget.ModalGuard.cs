using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// Guard for DTE calls that can pop a modal dialog on the VS main thread (debugger
    /// stop/launch/restart, run-to-cursor, set-next-statement, ENC/Hot Reload). Two layers:
    /// <c>DTE.SuppressUI</c> is turned on for the duration (suppresses the prompts DTE honors),
    /// and a free-threaded watchdog watches for "main window disabled by a modal" and fails the
    /// RPC with a typed WrongState error naming the dialog instead of hanging the pipe forever.
    /// The underlying DTE call keeps running on the main thread until the dialog is dismissed
    /// in VS — the error text says so.
    /// </summary>
    private async Task RunWithModalGuardAsync(string operation, Func<EnvDTE80.DTE2, Task> body, CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        var dte = await RequireDteAsync();

        IntPtr mainHwnd = IntPtr.Zero;
        try { mainHwnd = (IntPtr)dte.MainWindow.HWnd; } catch { }

        bool prevSuppress = false, suppressed = false;
        try { prevSuppress = dte.SuppressUI; dte.SuppressUI = true; suppressed = true; } catch { }

        // Hop OFF the main thread before starting the body. If we stayed on it, JTF would run
        // the body inline and a synchronous modal pump inside it would block this method before
        // the watchdog ever starts.
        await TaskScheduler.Default;

        // The body runs (and SuppressUI is restored) on the main thread, when the body actually
        // finishes — restoring from another thread would deadlock behind the very modal we detect.
        var bodyTask = _jtf.RunAsync(async () =>
        {
            await _jtf.SwitchToMainThreadAsync(ct);
            try { await body(dte); }
            finally { if (suppressed) { try { dte.SuppressUI = prevSuppress; } catch { } } }
        }).Task;

        if (mainHwnd == IntPtr.Zero)
        {
            await bodyTask.ConfigureAwait(false);
            return;
        }

        var wedge = WatchForModalAsync(mainHwnd, bodyTask, ct);
        var first = await Task.WhenAny(bodyTask, wedge).ConfigureAwait(false);
        if (first == bodyTask)
        {
            await bodyTask.ConfigureAwait(false); // propagate body exceptions
            return;
        }

        var title = await wedge.ConfigureAwait(false);
        if (title is null)
        {
            await bodyTask.ConfigureAwait(false);
            return;
        }

        throw new VsmcpException(ErrorCodes.WrongState,
            $"{operation} is blocked by a modal dialog in Visual Studio: \"{title}\". " +
            "Dismiss the dialog in the VS window — the underlying operation is still pending there and completes once dismissed.");
    }

    /// <summary>
    /// Free-threaded. Resolves with the dialog's title once the VS main window has been disabled
    /// by a modal for ~3s continuously (outlasts transient wait dialogs), or null when the body
    /// completes first.
    /// </summary>
    private static async Task<string?> WatchForModalAsync(IntPtr mainHwnd, Task bodyTask, CancellationToken ct)
    {
        const int pollMs = 250;
        const int consecutiveForWedge = 12;
        int consecutive = 0;

        while (!bodyTask.IsCompleted)
        {
            try { await Task.Delay(pollMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }

            if (ModalNative.IsWindow(mainHwnd) && !ModalNative.IsWindowEnabled(mainHwnd))
            {
                if (++consecutive >= consecutiveForWedge)
                    return ModalNative.FindActiveDialogTitle(mainHwnd) ?? "(unidentified dialog)";
            }
            else
            {
                consecutive = 0;
            }
        }
        return null;
    }

    private static class ModalNative
    {
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

        /// <summary>Topmost visible, enabled, titled window of this process other than the main
        /// window — with the main window disabled, that is the modal.</summary>
        public static string? FindActiveDialogTitle(IntPtr mainHwnd)
        {
            string? found = null;
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows((h, _) =>
            {
                if (h == mainHwnd) return true;
                if (!IsWindowVisible(h) || !IsWindowEnabled(h)) return true;
                GetWindowThreadProcessId(h, out var pid);
                if (pid != myPid) return true;
                var sb = new StringBuilder(256);
                if (GetWindowText(h, sb, sb.Capacity) > 0)
                {
                    found = sb.ToString();
                    return false; // stop at the topmost titled candidate
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
