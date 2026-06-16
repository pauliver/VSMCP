namespace VSMCP.Shared;

/// <summary>
/// Base for list-style result DTOs (#129). Carries the one field every truncatable result
/// shares — <see cref="Truncated"/> — so it isn't redeclared per type. Per-result totals keep
/// their specific names (TotalReferences, TotalDiagnostics, TotalIndexed, …). Serialization is
/// unchanged: inherited public properties serialize identically to declared ones.
/// </summary>
public abstract class ResultBase
{
    /// <summary>True when more results existed than were returned.</summary>
    public bool Truncated { get; set; }
}
