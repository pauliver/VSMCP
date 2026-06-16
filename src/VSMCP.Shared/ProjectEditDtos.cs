using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class InvestigateSymbolResult
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Declaration { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<string> Subclasses { get; set; } = new();
    public List<string> Members { get; set; } = new();
    public int UsageCount { get; set; }
}

public sealed class CallGraphNode
{
    public string Name { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public List<CallGraphNode> Calls { get; set; } = new();
    public List<CallGraphNode> CalledBy { get; set; } = new();
}

public sealed class CallGraphResult
{
    public CallGraphNode? Root { get; set; }
}
