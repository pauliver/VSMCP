using System.Collections.Generic;

namespace VSMCP.Shared
{
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
}