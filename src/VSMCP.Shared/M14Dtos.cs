using System.Collections.Generic;

namespace VSMCP.Shared;

// M14: Bulk Operations - File data types

public sealed class FileWriteEntry
{
    public string Path { get; set; } = "";
    public string? Content { get; set; }
    public FileRange? Range { get; set; }
    public string? Text { get; set; }
}

public sealed class FileReadResultItem
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public BatchItemError? Error { get; set; }
}

public sealed class FileWriteResultItem
{
    public string Path { get; set; } = "";
    public int Bytes { get; set; }
    public bool OpenInEditor { get; set; }
    public BatchItemError? Error { get; set; }
}

public sealed class ReplaceManyFileResult
{
    public string Path { get; set; } = "";
    public int Replacements { get; set; }
}

public sealed class ReplaceManyResult
{
    public int Matched { get; set; }
    public int Replaced { get; set; }
    public List<ReplaceManyFileResult> Files { get; set; } = new();
}

public sealed class CodeBatchResult
{
    public string File { get; set; } = "";
    public List<CodeSymbol> Symbols { get; set; } = new();
    public string? Language { get; set; }
    public BatchItemError? Error { get; set; }
}
