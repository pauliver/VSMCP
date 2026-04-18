using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    private readonly BuildCoordinator _builds = new();

    public Task<BuildHandle> BuildStartAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default)
        => StartBuildAsync(BuildAction.Build, configuration, platform, projectIds, cancellationToken);

    public Task<BuildHandle> BuildRebuildAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default)
        => StartBuildAsync(BuildAction.Rebuild, configuration, platform, projectIds, cancellationToken);

    public Task<BuildHandle> BuildCleanAsync(string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken cancellationToken = default)
        => StartBuildAsync(BuildAction.Clean, configuration, platform, projectIds, cancellationToken);

    private async Task<BuildHandle> StartBuildAsync(BuildAction action, string? configuration, string? platform, IReadOnlyList<string>? projectIds, CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var solution = dte.Solution;
        if (solution?.IsOpen != true)
            throw new VsmcpException(ErrorCodes.WrongState, "No solution is open.");

        var sb = solution.SolutionBuild
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "SolutionBuild unavailable.");

        if (!string.IsNullOrWhiteSpace(configuration))
            ActivateConfiguration(sb, configuration!, platform);

        var job = _builds.Register(action, configuration, platform, projectIds);

        var bm = await _package.GetServiceAsync(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager2
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "IVsSolutionBuildManager2 unavailable.");

        job.BuildManager = bm;
        ErrorHandler.ThrowOnFailure(bm.AdviseUpdateSolutionEvents(job, out var cookie));
        job.AdviseCookie = cookie;

        _builds.MarkRunning(job);

        try
        {
            if (projectIds is { Count: > 0 })
            {
                var cfgName = configuration ?? TryGetActiveConfigName(sb) ?? "Debug";
                foreach (var id in projectIds)
                {
                    var project = VsHelpers.RequireProject(solution, id);
                    var unique = project.UniqueName;
                    switch (action)
                    {
                        case BuildAction.Clean:
                            sb.BuildProject(cfgName, unique, WaitForBuildToFinish: false);
                            sb.Clean(WaitForCleanToFinish: false);
                            break;
                        case BuildAction.Rebuild:
                            sb.Clean(WaitForCleanToFinish: true);
                            sb.BuildProject(cfgName, unique, WaitForBuildToFinish: false);
                            break;
                        default:
                            sb.BuildProject(cfgName, unique, WaitForBuildToFinish: false);
                            break;
                    }
                }
            }
            else
            {
                switch (action)
                {
                    case BuildAction.Clean: sb.Clean(WaitForCleanToFinish: false); break;
                    case BuildAction.Rebuild: sb.Clean(WaitForCleanToFinish: true); sb.Build(WaitForBuildToFinish: false); break;
                    default: sb.Build(WaitForBuildToFinish: false); break;
                }
            }
        }
        catch (Exception ex)
        {
            UnadviseAndComplete(job, BuildState.Failed);
            throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to start build: {ex.Message}", ex);
        }

        return job.Handle;
    }

    public async Task<BuildStatus> BuildStatusAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var job = _builds.Require(buildId);
        MaybeFinalize(job);
        return _builds.Snapshot(job);
    }

    public async Task<BuildStatus> BuildWaitAsync(string buildId, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var job = _builds.Require(buildId);
        var status = await _builds.WaitAsync(job, timeoutMs, cancellationToken).ConfigureAwait(false);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        MaybeFinalize(job);
        return _builds.Snapshot(job);
    }

    public async Task<BuildStatus> BuildCancelAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var job = _builds.Require(buildId);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte)
        {
            try { dte.ExecuteCommand("Build.Cancel"); } catch { }
        }
        // UpdateSolution_Done with fCancelCommand=1 will transition state.
        return _builds.Snapshot(job);
    }

    public async Task<IReadOnlyList<BuildDiagnostic>> BuildErrorsAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var job = _builds.Require(buildId);
        MaybeFinalize(job);
        return job.Errors.AsReadOnly();
    }

    public async Task<IReadOnlyList<BuildDiagnostic>> BuildWarningsAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var job = _builds.Require(buildId);
        MaybeFinalize(job);
        return job.Warnings.AsReadOnly();
    }

    public async Task<BuildOutput> BuildOutputAsync(string buildId, string? pane, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var job = _builds.Require(buildId);
        MaybeFinalize(job);

        var paneName = string.IsNullOrWhiteSpace(pane) ? "Build" : pane!;
        string text = job.OutputText;
        if (string.IsNullOrEmpty(text))
            text = TryReadOutputPane(paneName) ?? "";

        return new BuildOutput { BuildId = buildId, Pane = paneName, Text = text };
    }

    // -------- helpers --------

    private void MaybeFinalize(BuildJob job)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (job.Completion.Task.IsCompleted) return;
        if (job.State is BuildState.Queued or BuildState.Running) return;

        CollectDiagnostics(job);
        job.OutputText = TryReadOutputPane("Build") ?? "";
        UnadviseAndComplete(job, job.State);
    }

    private void UnadviseAndComplete(BuildJob job, BuildState finalState)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (job.BuildManager is { } bm && job.AdviseCookie != 0)
        {
            try { bm.UnadviseUpdateSolutionEvents(job.AdviseCookie); } catch { }
            job.AdviseCookie = 0;
        }
        _builds.MarkCompleted(job, finalState);
    }

    private void CollectDiagnostics(BuildJob job)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        EnvDTE80.DTE2? dte = null;
        try { dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2; } catch { }
        if (dte is null) return;

        try
        {
            var errorList = dte.ToolWindows.ErrorList;
            if (errorList?.ErrorItems is null) return;

            for (int i = 1; i <= errorList.ErrorItems.Count; i++)
            {
                EnvDTE80.ErrorItem item;
                try { item = errorList.ErrorItems.Item(i); } catch { continue; }
                if (item is null) continue;

                var severity = item.ErrorLevel switch
                {
                    EnvDTE80.vsBuildErrorLevel.vsBuildErrorLevelHigh => BuildSeverity.Error,
                    EnvDTE80.vsBuildErrorLevel.vsBuildErrorLevelMedium => BuildSeverity.Warning,
                    _ => BuildSeverity.Info,
                };

                string? file = null, project = null, description = null;
                int? line = null, col = null;
                try { file = item.FileName; } catch { }
                try { project = item.Project; } catch { }
                try { description = item.Description; } catch { }
                try { line = item.Line > 0 ? item.Line : null; } catch { }
                try { col = item.Column > 0 ? item.Column : null; } catch { }

                var diag = new BuildDiagnostic
                {
                    Severity = severity,
                    Message = description ?? "",
                    Project = string.IsNullOrEmpty(project) ? null : project,
                    File = string.IsNullOrEmpty(file) ? null : file,
                    Line = line,
                    Column = col,
                };

                if (severity == BuildSeverity.Error) job.Errors.Add(diag);
                else if (severity == BuildSeverity.Warning) job.Warnings.Add(diag);
            }
        }
        catch { }
    }

    private string? TryReadOutputPane(string paneName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            var panes = dte?.ToolWindows.OutputWindow.OutputWindowPanes;
            if (panes is null) return null;

            EnvDTE.OutputWindowPane? pane = null;
            try { pane = panes.Item(paneName); } catch { }
            if (pane is null) return null;

            var doc = pane.TextDocument;
            var point = doc.StartPoint.CreateEditPoint();
            return point.GetText(doc.EndPoint);
        }
        catch
        {
            return null;
        }
    }

    private static void ActivateConfiguration(EnvDTE.SolutionBuild sb, string configuration, string? platform)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            for (int i = 1; i <= sb.SolutionConfigurations.Count; i++)
            {
                var cfg = sb.SolutionConfigurations.Item(i) as EnvDTE80.SolutionConfiguration2;
                if (cfg is null) continue;
                if (!string.Equals(cfg.Name, configuration, StringComparison.OrdinalIgnoreCase)) continue;
                if (platform is not null && !string.Equals(cfg.PlatformName, platform, StringComparison.OrdinalIgnoreCase)) continue;
                cfg.Activate();
                return;
            }
        }
        catch { }
    }

    private static string? TryGetActiveConfigName(EnvDTE.SolutionBuild sb)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return (sb.ActiveConfiguration as EnvDTE80.SolutionConfiguration2)?.Name;
        }
        catch { return null; }
    }
}
