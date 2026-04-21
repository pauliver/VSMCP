using System;
using System.ComponentModel.Design;
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
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(VsmcpToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
public sealed class VSMCPPackage : AsyncPackage
{
    public const string PackageGuidString = "7e0b4e3e-0000-0000-0000-000000000001";

    public static VSMCPPackage? Instance { get; private set; }

    private PipeHost? _pipeHost;
    private ModuleTracker? _moduleTracker;
    private readonly HostActivity _activity = new HostActivity();
    private StatusBarReporter? _statusBar;
    private DiagEventCollector? _diagCollector;

    public VSMCPPackage()
    {
        Instance = this;
    }

    internal ModuleTracker? Modules => _moduleTracker;
    internal HostActivity Activity => _activity;
    internal DiagEventCollector? DiagEvents => _diagCollector;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var vsDebugger = await GetServiceAsync(typeof(SVsShellDebugger)) as IVsDebugger;
        _moduleTracker = new ModuleTracker(vsDebugger);
        _pipeHost = new PipeHost(this, JoinableTaskFactory, _activity);
        _pipeHost.Start();

        if (await GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar bar)
        {
            _statusBar = new StatusBarReporter(_activity, bar, JoinableTaskFactory);
        }

        if (await GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte2)
        {
            _diagCollector = new DiagEventCollector(dte2);
        }

        await VsmcpCommands.InitializeAsync(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusBar?.Dispose();
            _statusBar = null;
            _pipeHost?.Dispose();
            _pipeHost = null;
            _moduleTracker?.Dispose();
            _moduleTracker = null;
            _diagCollector?.Dispose();
            _diagCollector = null;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
        base.Dispose(disposing);
    }
}
