using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- NuGet management --------
    //
    // We avoid IVsPackageInstaller to keep the dependency surface small. Instead we read
    // <PackageReference> entries directly from the .csproj files (works for SDK-style projects)
    // and fall back to packages.config for legacy projects. nuget.add/remove edits the project
    // file textually and triggers VS to reload.

    public async Task<NuGetListResult> NugetListAsync(
        string? projectId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var result = new NuGetListResult();
        foreach (var p in VsHelpers.EnumerateProjects(dte.Solution))
        {
            if (projectId is not null
                && !string.Equals(p.UniqueName, projectId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p.Name, projectId, StringComparison.OrdinalIgnoreCase))
                continue;

            string? full = null;
            try { full = p.FullName; } catch { }
            if (string.IsNullOrEmpty(full) || !File.Exists(full)) continue;

            ReadProjectPackages(full!, p.UniqueName ?? p.Name ?? "", result.Packages);
        }
        return result;
    }

    private static void ReadProjectPackages(string projectPath, string projectId, List<NuGetPackage> into)
    {
        try
        {
            var xml = XDocument.Load(projectPath);
            foreach (var pr in xml.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var id = pr.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(id)) continue;
                var ver = pr.Attribute("Version")?.Value
                    ?? pr.Element(XName.Get("Version", pr.Name.NamespaceName))?.Value
                    ?? "";
                into.Add(new NuGetPackage { Id = id!, Version = ver, ProjectId = projectId });
            }
        }
        catch { }

        // Legacy packages.config (pre-SDK).
        try
        {
            var dir = Path.GetDirectoryName(projectPath)!;
            var pc = Path.Combine(dir, "packages.config");
            if (!File.Exists(pc)) return;
            var xml = XDocument.Load(pc);
            foreach (var pkg in xml.Descendants("package"))
            {
                var id = pkg.Attribute("id")?.Value;
                var ver = pkg.Attribute("version")?.Value;
                if (!string.IsNullOrEmpty(id))
                    into.Add(new NuGetPackage { Id = id!, Version = ver ?? "", ProjectId = projectId });
            }
        }
        catch { }
    }

    public async Task<NuGetActionResult> NugetAddAsync(
        string projectId, string packageId, string? version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId)) throw new VsmcpException(ErrorCodes.NotFound, "projectId is required.");
        if (string.IsNullOrEmpty(packageId)) throw new VsmcpException(ErrorCodes.NotFound, "packageId is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var path = project.FullName;
        if (!File.Exists(path)) throw new VsmcpException(ErrorCodes.NotFound, $"Project file not found: {path}");

        var xml = XDocument.Load(path);
        var ns = xml.Root!.Name.Namespace;
        var existing = xml.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "PackageReference"
            && string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (!string.IsNullOrEmpty(version))
                existing.SetAttributeValue("Version", version);
            xml.Save(path);
            return new NuGetActionResult
            {
                Success = true,
                Message = "Updated existing PackageReference.",
                Package = new NuGetPackage { Id = packageId, Version = version ?? "", ProjectId = projectId },
            };
        }

        // Locate first ItemGroup that has PackageReferences, or create one.
        var ig = xml.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "ItemGroup" && e.Elements().Any(c => c.Name.LocalName == "PackageReference"))
            ?? new XElement(ns + "ItemGroup");
        if (ig.Document is null) xml.Root!.Add(ig);

        var pr = new XElement(ns + "PackageReference",
            new XAttribute("Include", packageId));
        if (!string.IsNullOrEmpty(version)) pr.SetAttributeValue("Version", version);
        ig.Add(pr);
        xml.Save(path);

        return new NuGetActionResult
        {
            Success = true,
            Message = "Added PackageReference. VS will pick up the change on next reload/restore.",
            Package = new NuGetPackage { Id = packageId, Version = version ?? "", ProjectId = projectId },
        };
    }

    public async Task<NuGetActionResult> NugetRemoveAsync(
        string projectId, string packageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId)) throw new VsmcpException(ErrorCodes.NotFound, "projectId is required.");
        if (string.IsNullOrEmpty(packageId)) throw new VsmcpException(ErrorCodes.NotFound, "packageId is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var path = project.FullName;
        if (!File.Exists(path)) throw new VsmcpException(ErrorCodes.NotFound, $"Project file not found: {path}");

        var xml = XDocument.Load(path);
        var hit = xml.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "PackageReference"
            && string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        if (hit is null) return new NuGetActionResult { Success = false, Message = "PackageReference not found." };

        hit.Remove();
        xml.Save(path);
        return new NuGetActionResult
        {
            Success = true,
            Message = "Removed PackageReference.",
            Package = new NuGetPackage { Id = packageId, ProjectId = projectId },
        };
    }
}
