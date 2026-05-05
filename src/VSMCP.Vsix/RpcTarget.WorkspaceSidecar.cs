using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// Process-wide sidecar workspace for Open Folder mode. Static so it survives across
    /// pipe (re)connections — every new MCP client connection makes a fresh RpcTarget,
    /// but the projects loaded into the sidecar should persist for the VS session lifetime.
    /// </summary>
    private static WorkspaceSidecar? s_sidecar;
    private static readonly object s_sidecarLock = new();
    // Set of root paths we've auto-loaded so OnSolutionOpened doesn't redo a folder
    // every time the user closes/reopens the same workspace.
    private static readonly System.Collections.Generic.HashSet<string> s_autoLoadedRoots
        = new(StringComparer.OrdinalIgnoreCase);
    private static int s_autoLoadHooked; // 0 = not yet, 1 = wired

    private static WorkspaceSidecar Sidecar
    {
        get
        {
            lock (s_sidecarLock) return s_sidecar ??= new WorkspaceSidecar();
        }
    }

    /// <summary>
    /// Invoked once per process by RpcTarget's ctor to wire the workspace-watcher's
    /// SolutionOpened event into the sidecar auto-loader. Idempotent (interlocked guard).
    /// Also fires a one-shot probe in case the workspace was already open before we hooked
    /// (e.g., user launched VS directly with a folder argument).
    /// </summary>
    internal void TryEnableAutoLoad(WorkspaceWatcher watcher)
    {
        if (System.Threading.Interlocked.Exchange(ref s_autoLoadHooked, 1) != 0) return;
        watcher.SolutionOpened += root => MaybeAutoLoadRoot(root);

        // One-shot probe: if VS is already in Open Folder mode, kick off a load.
        _ = Task.Run(async () =>
        {
            try
            {
                await _jtf.SwitchToMainThreadAsync();
                if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is DTE2 dte
                    && dte.Solution is { IsOpen: true } sln
                    && !string.IsNullOrEmpty(sln.FullName)
                    && Directory.Exists(sln.FullName))
                {
                    var snapshot = sln.FullName;
                    // Hand off to a worker thread; LoadFolder walks disk.
                    _ = Task.Run(() => MaybeAutoLoadRoot(snapshot));
                }
            }
            catch { /* best-effort */ }
        });
    }

    private static void MaybeAutoLoadRoot(string? root)
    {
        if (string.IsNullOrEmpty(root)) return;
        if (!Directory.Exists(root)) return;
        lock (s_sidecarLock)
        {
            if (!s_autoLoadedRoots.Add(root!)) return;
        }
        try { Sidecar.LoadFolder(root!); } catch { /* best-effort */ }
    }

    /// <summary>Sidecar fall-through used by FindDocument when the live workspace doesn't have the file.</summary>
    private static Microsoft.CodeAnalysis.Document? FindDocumentInSidecar(string filePath)
    {
        WorkspaceSidecar? s;
        lock (s_sidecarLock) s = s_sidecar;
        return s?.FindDocument(filePath);
    }

    /// <summary>
    /// Concatenate the live VS workspace's projects with the sidecar workspace's projects (if any).
    /// Read-only enumeration tools (CodeFindSymbol, FileClasses, SearchText, etc.) should iterate
    /// this so Open Folder mode workspaces aren't blind to their own loaded csprojs.
    /// </summary>
    internal static System.Collections.Generic.IEnumerable<Microsoft.CodeAnalysis.Project> EnumerateAllProjects(
        Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace ws)
    {
        foreach (var p in ws.CurrentSolution.Projects) yield return p;
        WorkspaceSidecar? s;
        lock (s_sidecarLock) s = s_sidecar;
        if (s is not null)
            foreach (var p in s.Workspace.CurrentSolution.Projects) yield return p;
    }

    public async Task<ProjectLoadResult> ProjectLoadAsync(string csprojPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(csprojPath)) throw new VsmcpException(ErrorCodes.NotFound, "csprojPath is required.");
        await Task.Yield();
        var outcome = Sidecar.LoadProject(csprojPath);
        return ToDto(outcome);
    }

    public async Task<ProjectLoadFolderResult> ProjectLoadWorkspaceFolderAsync(string? rootPath, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        // If rootPath isn't supplied, infer it from the open Open-Folder workspace.
        if (string.IsNullOrEmpty(rootPath))
        {
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is DTE2 dte
                && dte.Solution is { IsOpen: true } sln
                && !string.IsNullOrEmpty(sln.FullName)
                && Directory.Exists(sln.FullName))
            {
                rootPath = sln.FullName;
            }
            else
            {
                throw new VsmcpException(ErrorCodes.NotFound,
                    "rootPath is required when the workspace path can't be inferred (no Open Folder workspace).");
            }
        }

        var outcome = Sidecar.LoadFolder(rootPath!);
        return new ProjectLoadFolderResult
        {
            Root = outcome.Root,
            TotalDocumentsAdded = outcome.TotalDocumentsAdded,
            Error = outcome.Error,
            Projects = outcome.Projects.Select(ToDto).ToList(),
        };
    }

    public Task<SidecarStatusResult> ProjectSidecarStatusAsync(CancellationToken cancellationToken = default)
    {
        WorkspaceSidecar? s;
        lock (s_sidecarLock) s = s_sidecar;
        if (s is null)
            return Task.FromResult(new SidecarStatusResult());
        return Task.FromResult(new SidecarStatusResult
        {
            LoadedProjectPaths = s.LoadedProjectPaths().ToList(),
            TotalDocuments = s.LoadedDocumentCount(),
        });
    }

    private static ProjectLoadResult ToDto(LoadProjectOutcome o) => new()
    {
        Path = o.Path,
        Loaded = o.Loaded,
        Reloaded = o.Reloaded,
        ProjectName = o.ProjectName,
        DocumentsAdded = o.DocumentsAdded,
        Error = o.Error,
    };
}
