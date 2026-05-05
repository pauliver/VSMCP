using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>Result of loading one .csproj into the in-process sidecar workspace.</summary>
public sealed class ProjectLoadResult
{
    public string Path { get; set; } = "";
    public bool Loaded { get; set; }
    public bool Reloaded { get; set; }
    public string? ProjectName { get; set; }
    public int DocumentsAdded { get; set; }
    public string? Error { get; set; }
}

/// <summary>Result of scanning a folder and loading all .csproj files found.</summary>
public sealed class ProjectLoadFolderResult
{
    public string Root { get; set; } = "";
    public List<ProjectLoadResult> Projects { get; set; } = new();
    public int TotalDocumentsAdded { get; set; }
    public string? Error { get; set; }
}

/// <summary>Snapshot of what's in the sidecar workspace right now.</summary>
public sealed class SidecarStatusResult
{
    public List<string> LoadedProjectPaths { get; set; } = new();
    public int TotalDocuments { get; set; }
}
