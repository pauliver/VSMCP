using System.Collections.Generic;
using System.Linq;

namespace VSMCP.Shared;

// File move / rename DTOs.
// One pair per move; result enumerates outcomes including dry-run plans.

public sealed class FileMovePair
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class FileMoveOutcome
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";

    /// <summary>True when the file was actually moved on disk. False in dry-run or when skipped.</summary>
    public bool Moved { get; set; }

    /// <summary>True when an owning .csproj's <c>&lt;Compile Include&gt;</c> entry was updated.</summary>
    public bool ProjectUpdated { get; set; }

    /// <summary>Path of the .csproj that was edited, or null when no edit was needed/applied.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Set when this pair was skipped or rejected. Null on success.</summary>
    public string? Skipped { get; set; }

    /// <summary>Set when this pair's mutation failed (and was rolled back). Null on success.</summary>
    public string? Error { get; set; }
}

public sealed class FileMoveManyResult
{
    public List<FileMoveOutcome> Outcomes { get; set; } = new();
    public bool DryRun { get; set; }

    public int Total => Outcomes.Count;
    public int MovedCount => Outcomes.Count(o => o.Moved);
    public int CsprojEdits => Outcomes.Count(o => o.ProjectUpdated);
    public int SkippedCount => Outcomes.Count(o => o.Skipped is not null);
    public int ErrorCount => Outcomes.Count(o => o.Error is not null);
}
