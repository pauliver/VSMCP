using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>
/// Per-item result in a batch call. Exactly one of <see cref="Value"/> / <see cref="Error"/> is set.
/// Preserves input ordering via <see cref="Index"/> so callers can correlate without threading IDs.
/// </summary>
public sealed class BatchItemResult<T>
{
    public int Index { get; set; }
    public bool Success { get; set; }
    public T? Value { get; set; }
    public BatchItemError? Error { get; set; }
}

public sealed class BatchItemError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class BatchResult<T>
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<BatchItemResult<T>> Items { get; set; } = new();
}

public sealed class FileReadRequest
{
    public string Path { get; set; } = "";
    public FileRange? Range { get; set; }
}

public sealed class MemoryReadRequest
{
    public string Address { get; set; } = "";
    public int Length { get; set; }
}
