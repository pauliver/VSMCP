using System.Collections.Generic;

namespace VSMCP.Shared;

// M15: Refactoring & Editing

public sealed class RenameLocation
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string CurrentText { get; set; } = "";
}

public sealed class RenameResult
{
    public List<RenameLocation> Locations { get; set; } = new();
    public List<RenameLocation> Conflicts { get; set; } = new();
}

public sealed class OrganizeUsingsResult
{
    public int Changes { get; set; }
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
}

public sealed class InsertResult
{
    public int Line { get; set; }
    public string Text { get; set; } = "";
    public bool OpenInEditor { get; set; }
}

public sealed class ReplaceMemberResult
{
    public bool Replaced { get; set; }
    public int Line { get; set; }
    public bool OpenInEditor { get; set; }
}

public sealed class MoveTypeResult
{
    public bool Success { get; set; }
    public CodeSpan? NewLocation { get; set; }
    public bool Conflict { get; set; }
}









// #137: apply a unified diff through the editor.
public sealed class ApplyPatchFileResult
{
    public string Path { get; set; } = "";
    public bool Applied { get; set; }
    public int HunksApplied { get; set; }
    public int HunksFailed { get; set; }
    public string? Error { get; set; }
    /// <summary>On dryRun, the would-be new file content; null otherwise.</summary>
    public string? PreviewText { get; set; }
}

public sealed class ApplyPatchResult
{
    public bool Success { get; set; }
    public int FilesChanged { get; set; }
    public List<ApplyPatchFileResult> Files { get; set; } = new();
}
