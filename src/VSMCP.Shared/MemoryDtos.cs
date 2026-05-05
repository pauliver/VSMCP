using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class MemoryReadResult
{
    public string Address { get; set; } = "";
    public int RequestedBytes { get; set; }
    public int ReadBytes { get; set; }
    public int UnreadableBytes { get; set; }
    /// <summary>Lowercase hex, one byte as two chars (no separators). Empty when nothing was read.</summary>
    public string Hex { get; set; } = "";
    /// <summary>The same bytes as Base64, for programmatic use.</summary>
    public string Base64 { get; set; } = "";
}

public sealed class MemoryWriteResult
{
    public string Address { get; set; } = "";
    public int WrittenBytes { get; set; }
}

public sealed class RegisterInfo
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Type { get; set; }
}

public sealed class RegisterGroupInfo
{
    public string Name { get; set; } = "";
    public List<RegisterInfo> Registers { get; set; } = new();
}

public sealed class RegistersResult
{
    public int? ThreadId { get; set; }
    public int? FrameIndex { get; set; }
    public List<RegisterGroupInfo> Groups { get; set; } = new();
}

public sealed class DisasmInstruction
{
    public string Address { get; set; } = "";
    /// <summary>Opcode bytes as lowercase hex (e.g. "48 89 e5").</summary>
    public string? Bytes { get; set; }
    public string? Opcode { get; set; }
    public string? Operands { get; set; }
    public string? Symbol { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
}

public sealed class DisasmResult
{
    public string StartAddress { get; set; } = "";
    public int RequestedCount { get; set; }
    public List<DisasmInstruction> Instructions { get; set; } = new();
}
