using System.Collections.Generic;

namespace VSMCP.Shared;

// M18: Semantic Code Layer

public sealed class SymbolMatch
{
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Signature { get; set; }
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
}

public sealed class SymbolMatchResult
{
    public List<SymbolMatch> Matches { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

public sealed class ReadMemberResult
{
    public string File { get; set; } = "";
    public string Content { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Signature { get; set; }
}










