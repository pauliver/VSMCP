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








