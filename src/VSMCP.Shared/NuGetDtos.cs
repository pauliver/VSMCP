using System.Collections.Generic;

namespace VSMCP.Shared
{
    public sealed class NuGetPackage
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string? ProjectId { get; set; }
    }
public sealed class NuGetListResult
{
    public List<NuGetPackage> Packages { get; set; } = new();
}
public sealed class NuGetActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public NuGetPackage? Package { get; set; }
}
}