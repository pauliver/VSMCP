using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class ThreadInfo
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Location { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsFrozen { get; set; }
    /// <summary>VS-reported state: Running, Break, Terminated, etc.</summary>
    public string? State { get; set; }
}

public sealed class ThreadListResult
{
    public List<ThreadInfo> Threads { get; set; } = new();
}

public sealed class StackFrameInfo
{
    /// <summary>0-based index from the top of the stack on its owning thread.</summary>
    public int Index { get; set; }
    public int ThreadId { get; set; }
    public string FunctionName { get; set; } = "";
    public string? Module { get; set; }
    public string? Language { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class StackGetResult
{
    public int ThreadId { get; set; }
    public List<StackFrameInfo> Frames { get; set; } = new();
    /// <summary>True if <see cref="Frames"/> was truncated at the requested depth.</summary>
    public bool Truncated { get; set; }
}

public sealed class VariableInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Value { get; set; }
    public bool IsExpandable { get; set; }
    /// <summary>When requested, the child expansion. Empty when not expanded.</summary>
    public List<VariableInfo> Children { get; set; } = new();
}

public sealed class VariableListResult
{
    public int ThreadId { get; set; }
    public int FrameIndex { get; set; }
    public List<VariableInfo> Variables { get; set; } = new();
}

public sealed class EvalOptions
{
    public string Expression { get; set; } = "";
    /// <summary>Thread to evaluate on. Defaults to the current thread.</summary>
    public int? ThreadId { get; set; }
    /// <summary>Frame index within the thread. Defaults to the current frame (index 0).</summary>
    public int? FrameIndex { get; set; }
    /// <summary>Must be true to allow function calls / property getters that may have side effects.</summary>
    public bool AllowSideEffects { get; set; }
    /// <summary>How many levels of children to expand for object values. 0 = no expansion (default).</summary>
    public int ExpandDepth { get; set; }
    /// <summary>Evaluation timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 5000;
}

public sealed class EvalResult
{
    public string Expression { get; set; } = "";
    public bool IsValid { get; set; }
    public string? Type { get; set; }
    public string? Value { get; set; }
    public bool IsExpandable { get; set; }
    public List<VariableInfo> Children { get; set; } = new();
}
