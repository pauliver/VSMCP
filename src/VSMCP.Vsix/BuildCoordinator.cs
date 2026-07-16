using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Tracks async solution builds started via <see cref="RpcTarget"/> so callers can poll
/// status, block on <see cref="WaitAsync"/>, or ask for diagnostics after completion.
///
/// Instances subscribe to <see cref="IVsUpdateSolutionEvents2"/> for the lifetime of a
/// build and self-unadvise when it ends.
/// </summary>
internal sealed class BuildCoordinator
{
    private readonly ConcurrentDictionary<string, BuildJob> _builds = new();

    public BuildJob Register(BuildAction action, string? configuration, string? platform, IReadOnlyList<string>? projects)
    {
        var job = new BuildJob
        {
            Handle = new BuildHandle
            {
                BuildId = Guid.NewGuid().ToString("N"),
                Action = action,
                Configuration = configuration,
                Platform = platform,
                Projects = projects is null ? new List<string>() : new List<string>(projects),
                StartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            },
            State = BuildState.Queued,
            Completion = new TaskCompletionSource<BuildStatus>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _builds[job.Handle.BuildId] = job;
        return job;
    }

    public BuildJob Require(string buildId)
    {
        if (!_builds.TryGetValue(buildId, out var job))
            throw new VsmcpException(ErrorCodes.NotFound, $"Unknown buildId '{buildId}'.");
        return job;
    }

    public bool TryGet(string buildId, out BuildJob job) => _builds.TryGetValue(buildId, out job!);

    /// <summary>The job currently Queued/Running, if any — build.start enforces one at a time.</summary>
    public bool TryGetActive(out BuildJob active)
    {
        foreach (var kv in _builds)
        {
            if (kv.Value.State is BuildState.Queued or BuildState.Running)
            {
                active = kv.Value;
                return true;
            }
        }
        active = null!;
        return false;
    }

    public BuildStatus Snapshot(BuildJob job) => new()
    {
        BuildId = job.Handle.BuildId,
        State = job.State,
        StartedAtMs = job.Handle.StartedAtMs,
        EndedAtMs = job.EndedAtMs,
        ErrorCount = job.Errors.Count,
        WarningCount = job.Warnings.Count,
        AllProjectsSucceeded = job.State switch
        {
            BuildState.Succeeded => true,
            BuildState.Failed => false,
            _ => null,
        },
    };

    public void MarkRunning(BuildJob job)
    {
        job.State = BuildState.Running;
    }

    public void MarkCompleted(BuildJob job, BuildState finalState)
    {
        job.State = finalState;
        job.EndedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        job.Completion.TrySetResult(Snapshot(job));
    }

    public async Task<BuildStatus> WaitAsync(BuildJob job, int? timeoutMs, CancellationToken ct)
    {
        if (job.Completion.Task.IsCompleted)
            return job.Completion.Task.Result;

        if (timeoutMs is null)
        {
            // Wait WITHOUT ever cancelling the job's shared Completion source. Cancelling a build.wait
            // must abort only this caller's wait — cancelling job.Completion would make MaybeFinalize
            // early-return on IsCompleted, so the build-events advise leaks and the still-advised job
            // overwrites its state on every future build (build.status then reports an unrelated build).
            var ctTask = Task.Delay(Timeout.Infinite, ct);
            var firstDone = await Task.WhenAny(job.Completion.Task, ctTask).ConfigureAwait(false);
            if (firstDone == job.Completion.Task)
                return await job.Completion.Task.ConfigureAwait(false);

            ct.ThrowIfCancellationRequested(); // ctTask won => cancellation requested
            return await job.Completion.Task.ConfigureAwait(false); // unreachable
        }

        var delay = Task.Delay(timeoutMs.Value, ct);
        var winner = await Task.WhenAny(job.Completion.Task, delay).ConfigureAwait(false);
        if (winner == job.Completion.Task)
            return await job.Completion.Task.ConfigureAwait(false);

        var snap = Snapshot(job);
        snap.State = BuildState.TimedOut;
        return snap;
    }
}

internal sealed class BuildJob : IVsUpdateSolutionEvents2
{
    public BuildHandle Handle { get; set; } = null!;
    public BuildState State { get; set; }
    public long? EndedAtMs { get; set; }
    public TaskCompletionSource<BuildStatus> Completion { get; set; } = null!;
    public List<BuildDiagnostic> Errors { get; } = new();
    public List<BuildDiagnostic> Warnings { get; } = new();
    /// <summary>Output-pane text captured after the build finishes.</summary>
    public string OutputText { get; set; } = "";

    public uint AdviseCookie { get; set; }
    public IVsSolutionBuildManager2? BuildManager { get; set; }

    /// <summary>
    /// Invoked from <see cref="UpdateSolution_Done"/> so the owner (RpcTarget) can
    /// collect diagnostics, unadvise, and signal <see cref="Completion"/>. Runs on
    /// the VS UI thread because IVsUpdateSolutionEvents2 fires there.
    /// </summary>
    public Action<BuildJob>? OnDone { get; set; }

    /// <summary>
    /// Correlation guard: set when a build BEGINS while this job is advised. A build that was
    /// already running when we advised delivers only its Done — without this flag that stray
    /// Done would complete this job with another build's outcome.
    /// </summary>
    private bool _sawBegin;

    // -------- IVsUpdateSolutionEvents2 --------

    public int UpdateSolution_Begin(ref int pfCancelUpdate)
    {
        _sawBegin = true;
        return 0;
    }

    public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
    {
        if (!_sawBegin) return 0; // not our build — see _sawBegin

        var final = fCancelCommand != 0 ? BuildState.Canceled
                  : fSucceeded != 0 ? BuildState.Succeeded
                  : BuildState.Failed;
        State = final;
        EndedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try { OnDone?.Invoke(this); } catch { }
        return 0;
    }

    public int UpdateSolution_StartUpdate(ref int pfCancelUpdate)
    {
        _sawBegin = true;
        return 0;
    }
    public int UpdateSolution_Cancel() => 0;
    public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => 0;
    public int UpdateProjectCfg_Begin(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, ref int pfCancel) => 0;
    public int UpdateProjectCfg_Done(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, int fSuccess, int fCancel) => 0;
}
