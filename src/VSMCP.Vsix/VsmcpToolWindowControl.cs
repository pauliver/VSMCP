using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.Threading;
using IoPath = System.IO.Path;

namespace VSMCP.Vsix;

/// <summary>
/// WPF surface for the VSMCP tool window. No XAML — built in code so the VSIX
/// project (classic csproj) doesn't need Page/Markup build actions wired up.
/// Binds to <see cref="HostActivity"/> and repaints on Changed plus a 1s timer
/// (the timer keeps the "N seconds ago" readout live when nothing else happens).
/// </summary>
internal sealed class VsmcpToolWindowControl : UserControl
{
    private readonly HostActivity _activity;
    private readonly JoinableTaskFactory _jtf;
    private readonly DispatcherTimer _tick;

    private readonly Ellipse _statusDot;
    private readonly TextBlock _statusText;
    private readonly TextBlock _pipeName;
    private readonly TextBlock _lastActivity;
    private readonly TextBlock _counts;
    private readonly TextBlock _lastError;
    private readonly CheckBox _autoFocus;
    private readonly ListBox _recent;

    public VsmcpToolWindowControl(HostActivity activity, JoinableTaskFactory jtf)
    {
        _activity = activity;
        _jtf = jtf;
        Padding = new Thickness(8);
        Background = SystemColors.WindowBrush;

        var root = new DockPanel { LastChildFill = true };

        // --- Header: status dot + text ---
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _statusDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.Gray,
            Margin = new Thickness(0, 2, 6, 0),
        };
        _statusText = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(_statusDot);
        header.Children.Add(_statusText);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // --- Details ---
        var details = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        _pipeName = AddRow(details, "Pipe:", out var pipePanel);
        var copyBtn = new Button { Content = "Copy", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0) };
        copyBtn.Click += (_, __) =>
        {
            try { Clipboard.SetText(_activity.PipeName); } catch { }
        };
        pipePanel.Children.Add(copyBtn);

        _lastActivity = AddRow(details, "Last activity:");
        _counts = AddRow(details, "RPCs:");
        _lastError = AddRow(details, "Last error:");
        _lastError.Foreground = Brushes.Firebrick;

        _autoFocus = new CheckBox
        {
            Content = "Auto-focus VS after each RPC (teaching mode)",
            Margin = new Thickness(0, 4, 0, 0),
            IsChecked = true,
        };
        _autoFocus.Checked += (_, __) => ApplyAutoFocus(true);
        _autoFocus.Unchecked += (_, __) => ApplyAutoFocus(false);
        details.Children.Add(_autoFocus);

        var logsBtn = new Button
        {
            Content = "Open logs folder",
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        logsBtn.Click += (_, __) => OpenLogsFolder();
        details.Children.Add(logsBtn);

        DockPanel.SetDock(details, Dock.Top);
        root.Children.Add(details);

        // --- Recent RPCs ---
        var recentHeader = new TextBlock
        {
            Text = "Recent RPCs",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 4),
        };
        DockPanel.SetDock(recentHeader, Dock.Top);
        root.Children.Add(recentHeader);

        _recent = new ListBox
        {
            BorderThickness = new Thickness(1),
            BorderBrush = SystemColors.ActiveBorderBrush,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
        };
        root.Children.Add(_recent);

        Content = root;

        _activity.Changed += OnActivityChanged;

        _tick = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _tick.Tick += (_, __) => Refresh();
        _tick.Start();

        Unloaded += (_, __) =>
        {
            _tick.Stop();
            _activity.Changed -= OnActivityChanged;
        };

        Refresh();
    }

    private static TextBlock AddRow(StackPanel panel, string label)
        => AddRow(panel, label, out _);

    private static TextBlock AddRow(StackPanel panel, string label, out StackPanel row)
    {
        row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 100,
            Foreground = Brushes.Gray,
        });
        var value = new TextBlock { Text = "" };
        row.Children.Add(value);
        panel.Children.Add(row);
        return value;
    }

    private void ApplyAutoFocus(bool enabled)
    {
        // Fire-and-forget: the checkbox is authoritative, but we only have access
        // to the package via RpcTarget on demand. Walk up through the package.
        try
        {
            if (FindPackage() is { } pkg)
            {
                // Cheap: ask any future RpcTarget via a shared flag. For now we just
                // mirror onto a static toggle that new RpcTargets pick up at ctor time.
                VsmcpGlobalDefaults.AutoFocusDefault = enabled;
            }
        }
        catch { }
    }

    private VSMCPPackage? FindPackage()
    {
        // The control's Tag is not set; walk via the Package service if possible.
        // This path runs on the UI thread so GetGlobalService is fine.
        try
        {
            return Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(VSMCPPackage)) as VSMCPPackage;
        }
        catch { return null; }
    }

    private void OnActivityChanged(object? sender, EventArgs e)
    {
        // Changed fires from the pipe host thread. Route through JTF so we always
        // land on the VS main UI thread, even if this Control's Dispatcher somehow
        // points elsewhere (e.g., async tool-window restore quirks).
        _jtf.RunAsync(async () =>
        {
            await _jtf.SwitchToMainThreadAsync();
            Refresh();
        }).Task.Forget();
    }

    private void Refresh()
    {
        var snap = _activity.GetSnapshot();

        _pipeName.Text = string.IsNullOrEmpty(snap.PipeName) ? "(not started)" : snap.PipeName;

        var age = snap.LastActivityUtc == default
            ? "never"
            : FormatAge(DateTime.UtcNow - snap.LastActivityUtc);
        _lastActivity.Text = age;
        _counts.Text = $"{snap.RpcCount} total, {snap.RpcErrorCount} errors";
        _lastError.Text = snap.LastError ?? "—";

        // Traffic-light: green connected & recent, yellow connected but idle >30s,
        // red error in last 5s, gray never connected.
        var idleSeconds = snap.LastActivityUtc == default ? double.MaxValue : (DateTime.UtcNow - snap.LastActivityUtc).TotalSeconds;
        if (snap.ClientCount > 0 && idleSeconds < 30)
        {
            _statusDot.Fill = Brushes.LimeGreen;
            _statusText.Text = $"Connected ({snap.ClientCount} client{(snap.ClientCount == 1 ? "" : "s")})";
        }
        else if (snap.ClientCount > 0)
        {
            _statusDot.Fill = Brushes.Gold;
            _statusText.Text = $"Connected, idle {FormatAge(TimeSpan.FromSeconds(idleSeconds))}";
        }
        else if (snap.RpcErrorCount > 0 && idleSeconds < 5)
        {
            _statusDot.Fill = Brushes.IndianRed;
            _statusText.Text = "Recent error";
        }
        else if (snap.RpcCount > 0)
        {
            _statusDot.Fill = Brushes.Gray;
            _statusText.Text = "Disconnected (idle)";
        }
        else
        {
            _statusDot.Fill = Brushes.LightGray;
            _statusText.Text = "Waiting for client";
        }

        // Rebuild recent list (small N, simple refresh is fine).
        _recent.Items.Clear();
        foreach (var e in snap.Recent)
        {
            var local = e.WhenUtc.ToLocalTime().ToString("HH:mm:ss");
            var line = string.IsNullOrEmpty(e.Error)
                ? $"{local}  {e.ElapsedMs,7:F1} ms  {e.Method}"
                : $"{local}  {e.ElapsedMs,7:F1} ms  {e.Method}   !! {e.Error}";
            var item = new ListBoxItem
            {
                Content = line,
                Foreground = string.IsNullOrEmpty(e.Error) ? Brushes.Black : Brushes.Firebrick,
            };
            _recent.Items.Add(item);
        }
    }

    private static string FormatAge(TimeSpan ts)
    {
        if (ts.TotalSeconds < 0) ts = TimeSpan.Zero;
        if (ts.TotalSeconds < 1) return "just now";
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s ago";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s ago";
        return $"{(int)ts.TotalHours}h {ts.Minutes}m ago";
    }

    private void OpenLogsFolder()
    {
        try
        {
            var dir = IoPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VSMCP", "logs");
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch { }
    }
}

/// <summary>
/// Global toggle the tool window flips when the auto-focus checkbox changes.
/// <see cref="RpcTarget"/> picks this up at construction time so newly-attached
/// clients respect the current UI setting. Existing clients are not reset.
/// </summary>
internal static class VsmcpGlobalDefaults
{
    public static bool AutoFocusDefault { get; set; } = true;
}
