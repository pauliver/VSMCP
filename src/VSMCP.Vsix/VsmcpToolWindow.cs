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
        Content = new VsmcpToolWindowControl(pkg.Activity);
    }
}
