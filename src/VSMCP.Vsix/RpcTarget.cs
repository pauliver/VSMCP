using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// JSON-RPC method surface. VS APIs touched here must run on the UI thread;
/// we switch at the top of each method so callers can stay free-threaded.
/// </summary>
internal sealed partial class RpcTarget : IVsmcpRpc
{
    private readonly VSMCPPackage _package;
    private readonly JoinableTaskFactory _jtf;

    public RpcTarget(VSMCPPackage package, JoinableTaskFactory jtf)
    {
        _package = package;
        _jtf = jtf;
    }

    public async Task<HandshakeResult> HandshakeAsync(int clientMajor, int clientMinor, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        string? edition = null;
        string? version = null;
        if (await _package.GetServiceAsync(typeof(SVsShell)) is IVsShell shell)
        {
            shell.GetProperty((int)__VSSPROPID5.VSSPROPID_ReleaseVersion, out var verObj);
            version = verObj as string;
            shell.GetProperty((int)__VSSPROPID5.VSSPROPID_AppBrandName, out var editionObj);
            edition = editionObj as string;
        }

        return new HandshakeResult
        {
            ProtocolMajor = ProtocolVersion.Major,
            ProtocolMinor = ProtocolVersion.Minor,
            ExtensionVersion = typeof(RpcTarget).Assembly.GetName().Version?.ToString(),
            VsProcessId = Process.GetCurrentProcess().Id,
            VsEdition = edition,
            VsVersion = version,
        };
    }

    public async Task<PingResult> PingAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return new PingResult
        {
            Message = "pong",
            ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    public async Task<VsStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var status = new VsStatus();

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return status;

        var solution = dte.Solution;
        if (solution?.IsOpen == true && !string.IsNullOrEmpty(solution.FullName))
        {
            status.SolutionOpen = true;
            status.SolutionPath = solution.FullName;
            status.SolutionName = Path.GetFileNameWithoutExtension(solution.FullName);

            try
            {
                var active = solution.SolutionBuild?.ActiveConfiguration as EnvDTE80.SolutionConfiguration2;
                status.ActiveConfiguration = active?.Name;
                status.ActivePlatform = active?.PlatformName;
            }
            catch { }

            try
            {
                if (solution.SolutionBuild?.StartupProjects is Array startup && startup.Length > 0
                    && startup.GetValue(0) is string sp)
                {
                    status.StartupProject = sp;
                }
            }
            catch { }

            foreach (EnvDTE.Project p in solution.Projects)
            {
                if (p is null) continue;
                try { status.LoadedProjects.Add(p.UniqueName ?? p.Name); } catch { }
            }
        }

        var debugger = dte.Debugger;
        if (debugger is not null)
        {
            status.Debugging = debugger.CurrentMode != EnvDTE.dbgDebugMode.dbgDesignMode;
            status.DebugMode = debugger.CurrentMode.ToString();
        }

        return status;
    }
}
