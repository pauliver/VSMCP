using System.Collections.Generic;

namespace VSMCP.Shared;

public enum SymbolState
{
    Unknown = 0,
    NotLoaded = 1,
    Loaded = 2,
    Stripped = 3,
}

public sealed class ModuleInfo
{
    /// <summary>Opaque module id minted by the VSIX. Use this with symbols.load/status.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? LoadAddress { get; set; }
    public int? Order { get; set; }
    public long? Size { get; set; }
    public string? Version { get; set; }
    public string? TimestampUtc { get; set; }
    public bool Is64Bit { get; set; }
    public bool IsUserCode { get; set; }
    public SymbolState SymbolState { get; set; }
    public string? SymbolStatusMessage { get; set; }
    public string? SymbolPath { get; set; }
}

public sealed class ModuleListResult
{
    public List<ModuleInfo> Modules { get; set; } = new();
}

public sealed class SymbolStatusResult
{
    public string ModuleId { get; set; } = "";
    public SymbolState State { get; set; }
    public string? Message { get; set; }
    public string? SymbolPath { get; set; }
}
