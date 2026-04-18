using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace VSMCP.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class VSMCPPackage : AsyncPackage
{
    public const string PackageGuidString = "7e0b4e3e-0000-0000-0000-000000000001";

    private PipeHost? _pipeHost;
    private ModuleTracker? _moduleTracker;

    internal ModuleTracker? Modules => _moduleTracker;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var vsDebugger = await GetServiceAsync(typeof(SVsShellDebugger)) as IVsDebugger;
        _moduleTracker = new ModuleTracker(vsDebugger);
        _pipeHost = new PipeHost(this, JoinableTaskFactory);
        _pipeHost.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeHost?.Dispose();
            _pipeHost = null;
            _moduleTracker?.Dispose();
            _moduleTracker = null;
        }
        base.Dispose(disposing);
    }
}
