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

public sealed class AddMemberResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int InsertedAtLine { get; set; }
    public bool OpenInEditor { get; set; }
}

public sealed class AddUsingResult
{
    public bool Added { get; set; }
    public bool AlreadyPresent { get; set; }
    public int InsertedAtLine { get; set; }
}

public sealed class RemoveUsingResult
{
    public bool Removed { get; set; }
    public bool WasPresent { get; set; }
}

public sealed class UsingSuggestion
{
    public string SymbolName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public double Confidence { get; set; }
}

public sealed class UsingSuggestionsResult
{
    public List<UsingSuggestion> Suggestions { get; set; } = new();
}

public sealed class AddIncludeResult
{
    public bool Added { get; set; }
    public bool AlreadyPresent { get; set; }
    public int InsertedAtLine { get; set; }
}

public sealed class NamespaceInfo
{
    public string Namespace { get; set; } = "";
    public string? RootNamespace { get; set; }
    public string SuggestedAbsolutePath { get; set; } = "";
}

public sealed class ScaffoldResult
{
    public string FilePath { get; set; } = "";
    public string Namespace { get; set; } = "";
    public bool AddedToProject { get; set; }
}

public sealed class CreateClassResult
{
    public string FilePath { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string ClassName { get; set; } = "";
    public List<string> GeneratedUsings { get; set; } = new();
    public List<string> GeneratedMembers { get; set; } = new();
    public bool AddedToProject { get; set; }
}

public sealed class CppCreateClassResult
{
    public string HeaderPath { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public bool AddedToProject { get; set; }
}
