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

public sealed class NavigateResult
{
    public bool Opened { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}

public sealed class SnippetLine
{
    public string Text { get; set; } = "";
    public int Number { get; set; }
}

public sealed class SnippetResult
{
    public List<string> Before { get; set; } = new();
    public SnippetLine Line { get; set; } = new();
    public List<string> After { get; set; } = new();
}

public sealed class RegionRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class RegionResult
{
    public bool Expanded { get; set; }
    public bool Collapsed { get; set; }
    public RegionRange Range { get; set; } = new();
}

public sealed class IncludeNavigationResult
{
    public IncludeNavigationResultFound Found { get; set; } = new();
    public IncludeNavigationNavigation Navigation { get; set; } = new();
}

public sealed class IncludeNavigationResultFound
{
    public string File { get; set; } = "";
    public int Line { get; set; }
}

public sealed class IncludeNavigationNavigation
{
    public int FromLine { get; set; }
    public int ToLine { get; set; }
}
