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
    // Host-wide (static): build state is VS-global, so it must be shared across connections and
    // survive a client reconnect — a per-connection coordinator would let two clients race the
    // solution build and would orphan running jobs on disconnect.
    private static readonly BuildCoordinator _builds = new();

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

        var bm = await _package.GetServiceAsync(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager2
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "IVsSolutionBuildManager2 unavailable.");

        // One build at a time, host-wide. Overlapping solution builds corrupt each other's state,
        // so reject with a typed busy error instead of interleaving. A tracked job whose build VS
        // is no longer running missed its Done event (or never started) — close it out as stale
        // rather than bricking build.start forever.
        int vsBusy = 0;
        try { bm.QueryBuildManagerBusy(out vsBusy); } catch { }
        if (_builds.TryGetActive(out var active))
        {
            if (vsBusy != 0)
                throw new VsmcpException(ErrorCodes.WrongState,
                    $"A build is already in progress (buildId {active.Handle.BuildId}, state {active.State}). Use build.wait or build.cancel first.");
            UnadviseAndComplete(active, BuildState.Failed);
        }
        else if (vsBusy != 0)
        {
            throw new VsmcpException(ErrorCodes.WrongState,
                "Visual Studio is already running a build (started outside VSMCP). Wait for it to finish.");
        }

        if (!string.IsNullOrWhiteSpace(configuration))
            ActivateConfiguration(sb, configuration!, platform);

        var job = _builds.Register(action, configuration, platform, projectIds);

        job.BuildManager = bm;
        job.OnDone = j => MaybeFinalize(j);
        ErrorHandler.ThrowOnFailure(bm.AdviseUpdateSolutionEvents(job, out var cookie));
        job.AdviseCookie = cookie;

        _builds.MarkRunning(job);

        try
        {
            if (projectIds is { Count: > 0 })
            {
                if (action == BuildAction.Clean)
                {
                    // Scoped clean via the build manager — DTE has no per-project clean, and the old
                    // fallback (solution-wide sb.Clean) silently discarded every project's outputs.
                    StartScopedClean(bm, solution, projectIds);
                }
                else
                {
                    var cfgName = configuration ?? TryGetActiveConfigName(sb) ?? "Debug";
                    foreach (var id in projectIds)
                    {
                        var project = VsHelpers.RequireProject(solution, id);
                        var unique = project.UniqueName;
                        switch (action)
                        {
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

    /// <summary>Clean ONLY the named projects via IVsSolutionBuildManager2. Completion arrives
    /// through the same UpdateSolution events as any other build op.</summary>
    private static void StartScopedClean(IVsSolutionBuildManager2 bm, EnvDTE.Solution solution, IReadOnlyList<string> projectIds)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var vsSolution = ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) as IVsSolution
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "IVsSolution unavailable.");

        var hiers = new IVsHierarchy[projectIds.Count];
        for (int i = 0; i < projectIds.Count; i++)
        {
            var project = VsHelpers.RequireProject(solution, projectIds[i]);
            ErrorHandler.ThrowOnFailure(vsSolution.GetProjectOfUniqueName(project.UniqueName, out hiers[i]));
        }

        ErrorHandler.ThrowOnFailure(bm.StartUpdateSpecificProjectConfigurations(
            (uint)hiers.Length, hiers, null,
            rgdwCleanFlags: null, rgdwBuildFlags: null, rgdwDeployFlags: null,
            dwFlags: (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_CLEAN,
            fSuppressUI: 0));
    }

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
