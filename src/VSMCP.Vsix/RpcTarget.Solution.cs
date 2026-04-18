using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<SolutionInfo> SolutionInfoAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var info = new SolutionInfo();
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return info;

        var solution = dte.Solution;
        if (solution?.IsOpen != true || string.IsNullOrEmpty(solution.FullName))
            return info;

        info.IsOpen = true;
        info.Path = solution.FullName;
        info.Name = Path.GetFileNameWithoutExtension(solution.FullName);

        try
        {
            var active = solution.SolutionBuild?.ActiveConfiguration as EnvDTE80.SolutionConfiguration2;
            info.ActiveConfiguration = active?.Name;
            info.ActivePlatform = active?.PlatformName;
        }
        catch { }

        try
        {
            if (solution.SolutionBuild?.StartupProjects is Array startup && startup.Length > 0
                && startup.GetValue(0) is string sp)
            {
                info.StartupProject = sp;
            }
        }
        catch { }

        foreach (var p in VsHelpers.EnumerateProjects(solution))
            info.Projects.Add(VsHelpers.ToInfo(p));

        return info;
    }

    public async Task<SolutionInfo> SolutionOpenAsync(string path, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Solution path is required.");
        if (!File.Exists(path))
            throw new VsmcpException(ErrorCodes.NotFound, $"Solution file not found: {path}");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        dte.Solution.Open(path);
        return await SolutionInfoAsync(cancellationToken);
    }

    public async Task SolutionCloseAsync(bool saveFirst, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return;

        var solution = dte.Solution;
        if (solution?.IsOpen != true) return;

        solution.Close(saveFirst);
    }
}
