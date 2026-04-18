using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<IReadOnlyList<ProjectInfo>> ProjectListAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var list = new List<ProjectInfo>();
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return list;

        foreach (var p in VsHelpers.EnumerateProjects(dte.Solution))
            list.Add(VsHelpers.ToInfo(p));
        return list;
    }

    public async Task<ProjectInfo> ProjectAddAsync(string templatePathOrExistingFile, string destinationPath, string? projectName, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(templatePathOrExistingFile))
            throw new VsmcpException(ErrorCodes.NotFound, "Template or project path is required.");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var solution = dte.Solution
            ?? throw new VsmcpException(ErrorCodes.WrongState, "No solution is open.");

        bool isExistingProject =
            File.Exists(templatePathOrExistingFile) &&
            (templatePathOrExistingFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
             || templatePathOrExistingFile.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
             || templatePathOrExistingFile.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
             || templatePathOrExistingFile.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase));

        EnvDTE.Project? added = null;

        if (isExistingProject)
        {
            added = solution.AddFromFile(templatePathOrExistingFile, Exclusive: false);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new VsmcpException(ErrorCodes.NotFound, "Destination path is required when adding from a template.");

            var name = string.IsNullOrWhiteSpace(projectName)
                ? Path.GetFileName(destinationPath.TrimEnd('\\', '/'))
                : projectName!;

            Directory.CreateDirectory(destinationPath);
            solution.AddFromTemplate(templatePathOrExistingFile, destinationPath, name, Exclusive: false);

            foreach (var p in VsHelpers.EnumerateProjects(solution))
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    added = p;
                    break;
                }
            }
        }

        if (added is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Project was added but could not be resolved.");

        return VsHelpers.ToInfo(added);
    }

    public async Task ProjectRemoveAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        dte.Solution.Remove(project);
    }

    public async Task<IReadOnlyList<PropertyValue>> ProjectPropertiesGetAsync(string projectId, IReadOnlyList<string>? keys, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var props = project.Properties;
        var results = new List<PropertyValue>();
        if (props is null) return results;

        if (keys is { Count: > 0 })
        {
            foreach (var key in keys)
            {
                var pv = TryReadProperty(props, key);
                if (pv is not null) results.Add(pv);
            }
        }
        else
        {
            foreach (EnvDTE.Property prop in props)
            {
                if (prop is null) continue;
                var pv = TryReadProperty(props, prop.Name);
                if (pv is not null) results.Add(pv);
            }
        }

        return results;
    }

    private static PropertyValue? TryReadProperty(EnvDTE.Properties props, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var p = props.Item(name);
            if (p is null) return null;
            string? value = null;
            bool readable = true;
            try { value = p.Value?.ToString(); } catch { readable = false; }
            return new PropertyValue { Name = name, Value = value, Readable = readable, Writable = true };
        }
        catch
        {
            return null;
        }
    }

    public async Task ProjectPropertiesSetAsync(string projectId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (values is null || values.Count == 0) return;

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var props = project.Properties
            ?? throw new VsmcpException(ErrorCodes.Unsupported, "Project does not expose properties.");

        foreach (var kv in values)
        {
            try
            {
                var prop = props.Item(kv.Key);
                if (prop is null) continue;
                prop.Value = kv.Value;
            }
            catch (Exception ex)
            {
                throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to set property '{kv.Key}': {ex.Message}", ex);
            }
        }
    }

    public async Task<ProjectItemRef> ProjectFileAddAsync(string projectId, string path, bool linkOnly, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "File path is required.");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var items = project.ProjectItems
            ?? throw new VsmcpException(ErrorCodes.Unsupported, "Project does not support items.");

        EnvDTE.ProjectItem added;
        if (linkOnly)
        {
            if (!File.Exists(path))
                throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");
            added = items.AddFromFile(path);
        }
        else
        {
            var projectDir = Path.GetDirectoryName(project.FullName) ?? "";
            var target = Path.IsPathRooted(path) ? path : Path.Combine(projectDir, path);
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, string.Empty);
            }
            added = items.AddFromFileCopy(target);
        }

        string? full = null;
        try { full = added.FileCount >= 1 ? added.FileNames[1] : null; } catch { }

        var projDir = Path.GetDirectoryName(project.FullName) ?? "";
        string rel = full is not null && full.StartsWith(projDir, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(projDir.Length).TrimStart('\\', '/')
            : path;

        return new ProjectItemRef
        {
            ProjectId = project.UniqueName ?? project.Name,
            RelativePath = rel,
            FullPath = full,
            Kind = ProjectItemKind.File,
        };
    }

    public async Task ProjectFileRemoveAsync(string projectId, string path, bool deleteFromDisk, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var item = VsHelpers.FindItem(project, path)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File '{path}' not found in project '{projectId}'.");

        string? fullPath = null;
        try { fullPath = item.FileCount >= 1 ? item.FileNames[1] : null; } catch { }

        if (deleteFromDisk)
            item.Delete();
        else
            item.Remove();

        if (deleteFromDisk && fullPath is not null && File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch { }
        }
    }

    public async Task<ProjectItemRef> ProjectFolderCreateAsync(string projectId, string path, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Folder path is required.");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        EnvDTE.ProjectItems? items = project.ProjectItems
            ?? throw new VsmcpException(ErrorCodes.Unsupported, "Project does not support folders.");

        EnvDTE.ProjectItem? current = null;
        foreach (var seg in segments)
        {
            EnvDTE.ProjectItem? match = null;
            foreach (EnvDTE.ProjectItem existing in items)
            {
                if (existing is null) continue;
                if (string.Equals(existing.Name, seg, StringComparison.OrdinalIgnoreCase))
                {
                    match = existing;
                    break;
                }
            }
            current = match ?? items.AddFolder(seg);
            items = current.ProjectItems;
            if (items is null) break;
        }

        if (current is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Failed to create folder.");

        string? full = null;
        try { full = current.FileCount >= 1 ? current.FileNames[1] : null; } catch { }

        return new ProjectItemRef
        {
            ProjectId = project.UniqueName ?? project.Name,
            RelativePath = string.Join("/", segments),
            FullPath = full,
            Kind = ProjectItemKind.Folder,
        };
    }
}
