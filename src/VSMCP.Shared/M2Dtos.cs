using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class SolutionInfo
{
    public bool IsOpen { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? ActiveConfiguration { get; set; }
    public string? ActivePlatform { get; set; }
    public string? StartupProject { get; set; }
    public List<ProjectInfo> Projects { get; set; } = new();
}

public sealed class ProjectInfo
{
    /// <summary>Stable identifier for the project within the solution (DTE UniqueName).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Kind { get; set; }
    public string? FullPath { get; set; }
    public string? OutputType { get; set; }
    public string? TargetFramework { get; set; }
}

/// <summary>
/// 1-based inclusive range for text edits. endLine == startLine &amp;&amp; endColumn == startColumn
/// is an empty insertion point.
/// </summary>
public sealed class FileRange
{
    public int StartLine { get; set; } = 1;
    public int StartColumn { get; set; } = 1;
    public int EndLine { get; set; } = 1;
    public int EndColumn { get; set; } = 1;
}

public sealed class FileReadResult
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public bool OpenInEditor { get; set; }
    public bool HasUnsavedChanges { get; set; }
}

public sealed class FileWriteResult
{
    public string Path { get; set; } = "";
    public int BytesWritten { get; set; }
    public bool WentThroughEditor { get; set; }
}

public enum ProjectItemKind
{
    Unknown = 0,
    File = 1,
    Folder = 2,
    Project = 3,
}

public sealed class ProjectItemRef
{
    public string ProjectId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string? FullPath { get; set; }
    public ProjectItemKind Kind { get; set; }
}

public sealed class PropertyValue
{
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public bool Readable { get; set; }
    public bool Writable { get; set; }
}
