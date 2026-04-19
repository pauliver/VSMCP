using System;
using System.ComponentModel.Design;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace VSMCP.Vsix;

/// <summary>
/// Registers the <c>Tools → VSMCP Panel</c> menu entry that surfaces <see cref="VsmcpToolWindow"/>.
/// </summary>
internal static class VsmcpCommands
{
    /// <summary>Command set GUID — referenced from the .vsct.</summary>
    public static readonly Guid CommandSet = new Guid("7e0b4e3e-0000-0000-0000-000000000020");

    /// <summary>Command id for "VSMCP Panel" — matches the .vsct.</summary>
    public const int ShowToolWindowCommandId = 0x0100;

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var mcs = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (mcs is null) return;

        var id = new CommandID(CommandSet, ShowToolWindowCommandId);
        var cmd = new MenuCommand((_, __) => ShowWindow(package), id);
        mcs.AddCommand(cmd);
    }

    private static void ShowWindow(AsyncPackage package)
    {
        package.JoinableTaskFactory.RunAsync(async () =>
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await package.ShowToolWindowAsync(
                typeof(VsmcpToolWindow),
                id: 0,
                create: true,
                cancellationToken: CancellationToken.None);
            if (window?.Frame is IVsWindowFrame frame)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
            }
        });
    }
}
