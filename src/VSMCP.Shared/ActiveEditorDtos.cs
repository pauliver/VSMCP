using System.Collections.Generic;

namespace VSMCP.Shared;

// M19: Active editor + workspace events + tests + NuGet

public sealed class ActiveEditorInfo
{
    public string? File { get; set; }
    public string? Language { get; set; }
    public bool IsDirty { get; set; }
    public int? CursorLine { get; set; }
    public int? CursorColumn { get; set; }
}

public sealed class EditorSelection
{
    public string File { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Text { get; set; } = "";
}










