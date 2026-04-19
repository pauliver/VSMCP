using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VSMCP.Vsix;

/// <summary>
/// "VSMCP" tool window — View → Other Windows → VSMCP (or Tools → VSMCP Panel).
/// Shows live pipe activity so an observer can see what the AI just did.
/// </summary>
[Guid(WindowGuidString)]
public sealed class VsmcpToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "7e0b4e3e-0000-0000-0000-000000000010";

    public VsmcpToolWindow() : base(null)
    {
        Caption = "VSMCP";
    }

    protected override void Initialize()
    {
        base.Initialize();
        var pkg = (Package as VSMCPPackage) ?? VSMCPPackage.Instance;
        if (pkg is null)
            throw new InvalidOperationException("VSMCPPackage instance not available — tool window cannot bind to pipe activity.");

        // Force construction on the VS main UI thread. With an AsyncPackage, Initialize
        // can run on a pool thread when VS restores a persisted tool window — and the
        // WPF Control created there ends up with a Dispatcher pointing at that pool
        // thread, which never pumps. The tool window then appears to be "stuck" even
        // though HostActivity is being updated (cf. the StatusBarReporter which works
        // because it uses JTF directly). Marshaling once here guarantees the Control
        // and its Dispatcher live on the real UI thread.
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Content = new VsmcpToolWindowControl(pkg.Activity, ThreadHelper.JoinableTaskFactory);
        });
    }
}
