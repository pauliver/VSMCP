using System.Collections.Generic;

namespace VSMCP.Shared;

// M13: Search Operations

// Text search match
public sealed class TextMatch
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string LineText { get; set; } = "";
    public List<string> ContextBefore { get; set; } = new();
    public List<string> ContextAfter { get; set; } = new();
}

public sealed class TextSearchResult
{
    public List<TextMatch> Matches { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
    /// <summary>Path intern table (issue #78). When set, each Match.File is replaced by an integer-as-string into this map.</summary>
    public Dictionary<int, string>? PathTable { get; set; }
    /// <summary>Continuation cursor (issue #80). Pass as `cursor` to the next call; null when no more results.</summary>
    public string? NextCursor { get; set; }
    /// <summary>How many more results exist beyond this page (best-effort; -1 when unknown).</summary>
    public int RemainingCount { get; set; }
}

// Symbol search result
public sealed class SymbolSearchResult
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
    public string? Signature { get; set; }
}

public sealed class SymbolSearchResultContainer
{
    public List<SymbolSearchResult> Symbols { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Class search result
public sealed class ClassSearchResult
{
    public string Name { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Base { get; set; }
    public List<string> Interfaces { get; set; } = new();
}

public sealed class ClassSearchResultContainer
{
    public List<ClassSearchResult> Classes { get; set; } = new();
    public int Total { get; set; }
}

// Member search result
public sealed class MemberSearchResult
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Signature { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
}

public sealed class MemberSearchResultContainer
{
    public List<MemberSearchResult> Members { get; set; } = new();
    public int Total { get; set; }
}

// Usage result
public sealed class Usage
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Type { get; set; } = "";
}

public sealed class UsageResult
{
    public List<Usage> Usages { get; set; } = new();
    public int Total { get; set; }
}
