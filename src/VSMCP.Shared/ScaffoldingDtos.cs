using System.Collections.Generic;

namespace VSMCP.Shared
{
    public sealed class AddMemberResult
    {
        public string File { get; set; } = "";
        public string ClassName { get; set; } = "";
        public int InsertedAtLine { get; set; }
        public bool OpenInEditor { get; set; }
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
}