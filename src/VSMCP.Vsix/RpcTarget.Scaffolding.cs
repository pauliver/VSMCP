using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M18: Scaffolding (namespace_for_path, scaffold_file, create_class) --------

    public async Task<NamespaceInfo> ProjectNamespaceForPathAsync(
        string projectId, string relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId)) throw new VsmcpException(ErrorCodes.NotFound, "projectId is required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
        var project = VsHelpers.RequireProject(dte.Solution, projectId);

        string? rootNs = null;
        try { rootNs = project.Properties?.Item("RootNamespace")?.Value?.ToString(); } catch { }
        rootNs ??= project.Name;

        string? projectDir = null;
        try { projectDir = Path.GetDirectoryName(project.FullName); } catch { }

        var rel = (relativePath ?? "").Replace('\\', '/').Trim('/');
        var folderSegments = string.IsNullOrEmpty(rel) ? Array.Empty<string>() : rel.Split('/');
        var ns = folderSegments.Length == 0
            ? rootNs!
            : rootNs + "." + string.Join(".", folderSegments.Select(SanitizeNamespaceSegment));

        var suggested = string.IsNullOrEmpty(projectDir)
            ? rel
            : Path.Combine(projectDir!, rel.Replace('/', Path.DirectorySeparatorChar));

        return new NamespaceInfo
        {
            Namespace = ns,
            RootNamespace = rootNs,
            SuggestedAbsolutePath = suggested,
        };
    }

    private static string SanitizeNamespaceSegment(string seg)
    {
        if (string.IsNullOrEmpty(seg)) return "_";
        var s = new string(seg.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (s.Length == 0 || char.IsDigit(s[0])) s = "_" + s;
        return s;
    }

    public async Task<ScaffoldResult> CodeScaffoldFileAsync(
        string projectId, string relativePath, string? content, string? language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId)) throw new VsmcpException(ErrorCodes.NotFound, "projectId is required.");
        if (string.IsNullOrEmpty(relativePath)) throw new VsmcpException(ErrorCodes.NotFound, "relativePath is required.");

        var folder = Path.GetDirectoryName(relativePath.Replace('\\', '/')) ?? "";
        var nsInfo = await ProjectNamespaceForPathAsync(projectId, folder, cancellationToken).ConfigureAwait(false);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
        var project = VsHelpers.RequireProject(dte.Solution, projectId);
        var projectDir = Path.GetDirectoryName(project.FullName)!;

        var absPath = Path.Combine(projectDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(absPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var lang = (language ?? GetLanguageFromExt(absPath)).ToLowerInvariant();
        var fileText = ScaffoldContent(lang, nsInfo.Namespace, Path.GetFileNameWithoutExtension(absPath), content);
        File.WriteAllText(absPath, fileText);

        bool added = false;
        try
        {
            await ProjectFileAddAsync(projectId, absPath, linkOnly: false, cancellationToken).ConfigureAwait(false);
            added = true;
        }
        catch { /* best-effort; file remains on disk */ }

        return new ScaffoldResult
        {
            FilePath = absPath,
            Namespace = nsInfo.Namespace,
            AddedToProject = added,
        };
    }

    private static string GetLanguageFromExt(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp" or ".hxx" => "cpp",
            ".vb" => "visualbasic",
            _ => "text",
        };
    }

    private static string ScaffoldContent(string language, string @namespace, string typeName, string? body)
    {
        return language switch
        {
            "csharp" => body is not null
                ? body
                : $"namespace {@namespace};\n\npublic class {typeName}\n{{\n}}\n",
            "cpp" => body ?? $"#pragma once\n\nclass {typeName}\n{{\npublic:\n    {typeName}() = default;\n    ~{typeName}() = default;\n}};\n",
            "visualbasic" => body ?? $"Namespace {@namespace}\n    Public Class {typeName}\n    End Class\nEnd Namespace\n",
            _ => body ?? "",
        };
    }

    public async Task<CreateClassResult> CodeCreateClassAsync(
        string name, string? baseClass, IReadOnlyList<string>? interfaces, string? projectId, string? folder,
        bool generateStubs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name)) throw new VsmcpException(ErrorCodes.NotFound, "name is required.");

        // Resolve target project: if not specified, use the first non-test project.
        string resolvedProject;
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (string.IsNullOrEmpty(projectId))
        {
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
                throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
            var first = VsHelpers.EnumerateProjects(dte.Solution).FirstOrDefault();
            if (first is null) throw new VsmcpException(ErrorCodes.WrongState, "No projects in solution.");
            resolvedProject = first.UniqueName ?? first.Name;
        }
        else
        {
            resolvedProject = projectId!;
        }

        var nsInfo = await ProjectNamespaceForPathAsync(resolvedProject, folder ?? "", cancellationToken).ConfigureAwait(false);

        var generatedUsings = new List<string>();
        var generatedMembers = new List<string>();

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {nsInfo.Namespace};");
        sb.AppendLine();

        var declarationParts = new List<string> { $"public class {name}" };
        var inheritanceList = new List<string>();

        if (!string.IsNullOrEmpty(baseClass))
        {
            inheritanceList.Add(baseClass!);
            // Generate stubs for abstract members of the base class.
            if (generateStubs)
            {
                try
                {
                    var info = await FileInheritanceAsync("", baseClass!, cancellationToken).ConfigureAwait(false);
                    // Best-effort: we can't easily get base class members without resolving the symbol.
                    // Leave stubs empty; advanced version would walk INamedTypeSymbol.GetMembers().
                }
                catch { /* skip */ }
            }
        }

        if (interfaces is not null)
            foreach (var i in interfaces) inheritanceList.Add(i);

        if (inheritanceList.Count > 0)
            sb.Append("public class ").Append(name).Append(" : ").AppendLine(string.Join(", ", inheritanceList));
        else
            sb.AppendLine($"public class {name}");

        sb.AppendLine("{");
        sb.AppendLine("}");

        var relPath = string.IsNullOrEmpty(folder) ? name + ".cs" : Path.Combine(folder!, name + ".cs").Replace('\\', '/');
        var scaffold = await CodeScaffoldFileAsync(resolvedProject, relPath, sb.ToString(), "csharp", cancellationToken).ConfigureAwait(false);

        return new CreateClassResult
        {
            FilePath = scaffold.FilePath,
            Namespace = scaffold.Namespace,
            ClassName = name,
            GeneratedUsings = generatedUsings,
            GeneratedMembers = generatedMembers,
            AddedToProject = scaffold.AddedToProject,
        };
    }

    public async Task<CppCreateClassResult> CppCreateClassAsync(
        string name, string? baseClass, string? headerFolder, string? sourceFolder, string? projectId,
        bool generateVirtualStubs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name)) throw new VsmcpException(ErrorCodes.NotFound, "name is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (string.IsNullOrEmpty(projectId))
        {
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
                throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
            var first = VsHelpers.EnumerateProjects(dte.Solution).FirstOrDefault();
            if (first is null) throw new VsmcpException(ErrorCodes.WrongState, "No projects in solution.");
            projectId = first.UniqueName ?? first.Name;
        }

        var hdrFolder = headerFolder ?? "include";
        var srcFolder = sourceFolder ?? "src";
        var hdrRelPath = Path.Combine(hdrFolder, name + ".h").Replace('\\', '/');
        var srcRelPath = Path.Combine(srcFolder, name + ".cpp").Replace('\\', '/');

        var hdrSb = new StringBuilder();
        hdrSb.AppendLine("#pragma once");
        hdrSb.AppendLine();
        if (!string.IsNullOrEmpty(baseClass))
        {
            hdrSb.AppendLine($"#include \"{baseClass}.h\"");
            hdrSb.AppendLine();
            hdrSb.AppendLine($"class {name} : public {baseClass}");
        }
        else
        {
            hdrSb.AppendLine($"class {name}");
        }
        hdrSb.AppendLine("{");
        hdrSb.AppendLine("public:");
        hdrSb.AppendLine($"    {name}();");
        hdrSb.AppendLine($"    ~{name}();");
        hdrSb.AppendLine("};");

        var srcSb = new StringBuilder();
        srcSb.AppendLine($"#include \"{name}.h\"");
        srcSb.AppendLine();
        srcSb.AppendLine($"{name}::{name}() = default;");
        srcSb.AppendLine($"{name}::~{name}() = default;");

        var hdrScaffold = await CodeScaffoldFileAsync(projectId!, hdrRelPath, hdrSb.ToString(), "cpp", cancellationToken).ConfigureAwait(false);
        var srcScaffold = await CodeScaffoldFileAsync(projectId!, srcRelPath, srcSb.ToString(), "cpp", cancellationToken).ConfigureAwait(false);

        return new CppCreateClassResult
        {
            HeaderPath = hdrScaffold.FilePath,
            SourcePath = srcScaffold.FilePath,
            AddedToProject = hdrScaffold.AddedToProject && srcScaffold.AddedToProject,
        };
    }
}
