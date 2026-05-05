using System.Collections.Generic;

namespace VSMCP.Shared
{
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
}