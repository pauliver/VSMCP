using System.Collections.Generic;

namespace VSMCP.Shared;

// File list item
public sealed class FileListItem
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "file";
    public string? Language { get; set; }
    public string? ProjectId { get; set; }
}

// File list result
public sealed class FileListResult
{
    public List<FileListItem> Files { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Symbol info (classes, namespaces)
public sealed class SymbolInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
    public List<SymbolInfo> Children { get; set; } = new();
}

public sealed class SymbolsResult
{
    public List<SymbolInfo> Symbols { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Member info
public sealed class MemberInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Signature { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Access { get; set; }
    public bool IsStatic { get; set; }
}

public sealed class MembersResult
{
    public List<MemberInfo> Members { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Inheritance info
public sealed class InheritanceInfo
{
    public string Name { get; set; } = "";
    public CodeSpan? Location { get; set; }
}

public sealed class InheritanceResult
{
    public List<InheritanceInfo> BaseTypes { get; set; } = new();
    public List<InheritanceInfo> DerivedTypes { get; set; } = new();
    public List<InheritanceInfo> ImplementedInterfaces { get; set; } = new();
    public HierarchyInfo? Hierarchy { get; set; }
}

public sealed class HierarchyInfo
{
    public int Depth { get; set; }
    public List<string> Path { get; set; } = new();
}

// Dependency info
public sealed class DependencyInfo
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Type { get; set; } = "";
}

public sealed class DependencyListResult
{
    public List<DependencyInfo> Includes { get; set; } = new();
    public int Total { get; set; }
}
