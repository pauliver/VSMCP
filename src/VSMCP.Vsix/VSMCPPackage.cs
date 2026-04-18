using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
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

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _pipeHost = new PipeHost(this, JoinableTaskFactory);
        _pipeHost.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeHost?.Dispose();
            _pipeHost = null;
        }
        base.Dispose(disposing);
    }
}
