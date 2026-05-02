using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // M12: File & Symbol Discovery

    public async Task<FileListResult> FileListAsync(string? projectId, string? folder, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var solution = dte.Solution;
        if (solution?.IsOpen != true)
            throw new VsmcpException(ErrorCodes.WrongState, "No solution is open.");

        var result = new FileListResult();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in VsHelpers.EnumerateProjects(solution))
        {
            if (projectId is not null && !project.IdEquals(projectId)) continue;

            cancellationToken.ThrowIfCancellationRequested();

            var projectPath = project.FullName;
            if (!string.IsNullOrEmpty(projectPath))
            {
                var projectDir = Path.GetDirectoryName(projectPath)!;

                if (string.IsNullOrEmpty(folder))
                {
                    CollectFilesFromProject(project, projectDir, pattern, kinds, maxResults, result, seenPaths);
                }
                else
                {
                    var folderPath = Path.Combine(projectDir, folder.Replace('\\', '/'));
                    CollectFilesFromFolder(project, folderPath, pattern, kinds, maxResults, result, seenPaths);
                }
            }
        }

        return result;
    }

    private static void CollectFilesFromProject(EnvDTE.Project project, string projectDir, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, FileListResult result, HashSet<string> seenPaths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        CollectFromProjectItems(project.ProjectItems, projectDir, pattern, kinds, maxResults, result, seenPaths);

        if (result.Files.Count >= maxResults)
        {
            result.Truncated = true;
            result.Total = result.Files.Count;
            result.Files = result.Files.Take(maxResults).ToList();
        }
    }

    private static void CollectFromProjectItems(EnvDTE.ProjectItems? items, string projectDir, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, FileListResult result, HashSet<string> seenPaths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (items is null) return;

        foreach (EnvDTE.ProjectItem item in items)
        {
            if (item is null) continue;

            for (short i = 1; i <= item.FileCount; i++)
            {
                string? file = null;
                try { file = item.FileNames[i]; } catch { }

                if (string.IsNullOrEmpty(file)) continue;

                if (!seenPaths.Add(file)) continue;

                var kind = GetItemKind(item);
                if (kinds is not null && !kinds.Contains(kind)) continue;

                if (pattern is not null && !MatchesGlob(file, pattern)) continue;

                var language = GetLanguage(item, file);

                result.Files.Add(new FileListItem
                {
                    Path = file,
                    Kind = kind,
                    Language = language,
                    ProjectId = GetProjectId(item, projectDir)
                });

                if (result.Files.Count >= maxResults)
                {
                    result.Truncated = true;
                    result.Total = result.Files.Count;
                    result.Files = result.Files.Take(maxResults).ToList();
                    return;
                }
            }

            CollectFromProjectItems(item.ProjectItems, projectDir, pattern, kinds, maxResults, result, seenPaths);
        }
    }

    private static void CollectFilesFromFolder(EnvDTE.Project project, string folderPath, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, FileListResult result, HashSet<string> seenPaths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Directory.Exists(folderPath)) return;

        var projectDir = Path.GetDirectoryName(project.FullName)!;
        var relativePath = GetRelativePath(folderPath, projectDir);

        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (!seenPaths.Add(file)) continue;

            var kind = "file";
            if (kinds is not null && !kinds.Contains(kind)) continue;

            if (pattern is not null && !MatchesGlob(file, pattern)) continue;

            var language = GetLanguage(file);

            result.Files.Add(new FileListItem
            {
                Path = file,
                Kind = kind,
                Language = language,
                ProjectId = project.UniqueName ?? project.Name
            });

            if (result.Files.Count >= maxResults)
            {
                result.Truncated = true;
                result.Total = result.Files.Count;
                result.Files = result.Files.Take(maxResults).ToList();
                return;
            }
        }
    }

    private static string GetRelativePath(string fullPath, string baseDir)
    {
        var uri1 = new Uri(fullPath);
        var uri2 = new Uri(baseDir);
        return uri1.MakeRelativeUri(uri2).ToString().Replace('/', '\\');
    }

    private static bool IdEquals(this EnvDTE.Project project, string id)
    {
        return string.Equals(project.UniqueName, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(project.Name, id, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetItemKind(EnvDTE.ProjectItem item)
    {
        return item.SubProject is not null ? "project" : "file";
    }

    private static string GetLanguage(EnvDTE.ProjectItem item, string filePath)
    {
        try
        {
            if (item.ProjectItemKinds is string kind) return kind;
        }
        catch { }

        return GetLanguage(filePath);
    }

    private static string GetLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".h" or ".hpp" or ".hxx" => "cpp",
            ".vb" => "visualbasic",
            ".fs" => "fsharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            _ => "unknown"
        };
    }

    private static string GetProjectId(EnvDTE.ProjectItem item, string projectDir)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (item.ContainingProject is { } project)
            return project.UniqueName ?? project.Name;

        return string.Empty;
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".")
            .Replace("\\{", "(")
            .Replace("}", ")")
            .Replace(",", "|");

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    public async Task<SymbolsResult> FileClassesAsync(string? projectId, string? @namespace,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var ws = await GetWorkspaceAsync(cancellationToken);
        var result = new SymbolsResult();

        foreach (var project in ws.CurrentSolution.Projects)
        {
            if (projectId is not null && !project.Name.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var sm = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

                if (root is null || sm is null) continue;

                foreach (var node in root.ChildNodes())
                {
                    WalkSymbols(node, sm, result.Symbols, @namespace, kinds, maxResults, cancellationToken);
                    if (result.Symbols.Count >= maxResults)
                    {
                        result.Truncated = true;
                        break;
                    }
                }
            }
        }

        result.Total = result.Symbols.Count;
        if (result.Symbols.Count > maxResults)
        {
            result.Truncated = true;
            result.Symbols = result.Symbols.Take(maxResults).ToList();
        }

        return result;
    }

    private static void WalkSymbols(SyntaxNode node, SemanticModel sm, List<SymbolInfo> into,
        string? @namespace, IReadOnlyList<string>? kinds, int maxResults, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        switch (node)
        {
            case BaseNamespaceDeclarationSyntax ns:
            {
                var nsSym = sm.GetDeclaredSymbol(ns);
                var entry = nsSym is not null ? ToSymbolInfo(nsSym) : new SymbolInfo { Name = ns.Name.ToString(), Kind = "namespace" };
                entry.Container = @namespace;
                foreach (var c in ns.ChildNodes()) WalkSymbols(c, sm, into, entry.Name, kinds, maxResults, ct);
                return;
            }
            case BaseTypeDeclarationSyntax type:
            {
                var sym = sm.GetDeclaredSymbol(type);
                var entry = sym is not null ? ToSymbolInfo(sym) : new SymbolInfo { Name = type.Identifier.Text, Kind = "class" };
                if (@namespace is not null) entry.Container = @namespace;
                if (kinds is not null && !kinds.Contains(entry.Kind)) return;
                into.Add(entry);
                foreach (var member in type.ChildNodes()) WalkSymbols(member, sm, into, @namespace, kinds, maxResults, ct);
                return;
            }
        }
    }

    private static SymbolInfo ToSymbolInfo(ISymbol symbol)
    {
        return new SymbolInfo
        {
            Name = symbol.Name,
            Kind = symbol.Kind.ToString().ToLowerInvariant(),
            ContainerName = symbol.ContainingSymbol?.ToDisplayString(),
            Location = GetCodeSpan(symbol)
        };
    }

    private static CodeSpan? GetCodeSpan(ISymbol symbol)
    {
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null) return null;

        var span = loc.GetLineSpan();
        return new CodeSpan
        {
            File = span.Path ?? "",
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1
        };
    }

    public async Task<MembersResult> FileMembersAsync(string file, string className, IReadOnlyList<string>? kinds,
        bool excludeInherited, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrWhiteSpace(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file);
        if (doc is null) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var result = new MembersResult();

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || sm is null) return result;

        var classes = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Identifier.Text == className);

        foreach (var cls in classes)
        {
            var typeSym = sm.GetDeclaredSymbol(cls);
            if (typeSym is null) continue;

            foreach (var member in typeSym.GetMembers())
            {
                if (kinds is not null && !kinds.Contains(member.Kind.ToString().ToLowerInvariant())) continue;
                if (excludeInherited && member.DeclaredAccessibility != Accessibility.NotApplicable) continue;

                var span = cls.FullSpan;  // Simplified - would need actual member span
                var memberSpan = GetCodeSpan(member);

                result.Members.Add(new MemberInfo
                {
                    Name = member.Name,
                    Kind = member.Kind.ToString().ToLowerInvariant(),
                    Signature = member.ToDisplayString(),
                    Location = memberSpan
                });
            }
        }

        result.Total = result.Members.Count;

        return result;
    }

    public async Task<InheritanceResult> FileInheritanceAsync(string file, string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrWhiteSpace(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file);
        if (doc is null) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var result = new InheritanceResult();

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || sm is null) return result;

        var baseTypes = new List<InheritanceInfo>();
        var derivedTypes = new List<InheritanceInfo>();
        var interfaces = new List<InheritanceInfo>();

        var classes = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Identifier.Text == className);

        foreach (var cls in classes)
        {
            var typeSym = sm.GetDeclaredSymbol(cls);
            if (typeSym is null) continue;

            // Base types
            if (typeSym.BaseType is not null)
            {
                var baseSpan = GetCodeSpan(typeSym.BaseType);
                if (baseSpan is not null)
                {
                    baseTypes.Add(new InheritanceInfo { Name = typeSym.BaseType.Name, Location = baseSpan });
                }
            }

            // Interfaces
            foreach (var iface in typeSym.Interfaces)
            {
                var ifaceSpan = GetCodeSpan(iface);
                if (ifaceSpan is not null)
                {
                    interfaces.Add(new InheritanceInfo { Name = iface.Name, Location = ifaceSpan });
                }
            }
        }

        // Find derived types (reverse lookup)
        foreach (var project in ws.CurrentSolution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var docRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var docSm = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

                if (docRoot is null || docSm is null) continue;

                var derivedClasses = docRoot.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(t =>
                {
                    var dSym = docSm.GetDeclaredSymbol(t);
                    return dSym?.BaseType?.Name == className;
                });

                foreach (var dc in derivedClasses)
                {
                    var dSpan = GetCodeSpan(docSm.GetDeclaredSymbol(dc));
                    if (dSpan is not null)
                    {
                        derivedTypes.Add(new InheritanceInfo { Name = dc.Identifier.Text, Location = dSpan });
                    }
                }
            }
        }

        result.BaseTypes = baseTypes;
        result.DerivedTypes = derivedTypes;
        result.ImplementedInterfaces = interfaces;
        result.Hierarchy = new HierarchyInfo
        {
            Depth = baseTypes.Count + 1,
            Path = baseTypes.Select(b => b.Name).Append(className).ToList()
        };

        return result;
    }

    public async Task<FileListResult> FileGlobAsync(IReadOnlyList<string> patterns, string? projectId, CancellationToken cancellationToken = default)
    {
        if (patterns is null || patterns.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, "At least one pattern is required.");

        return await FileListAsync(projectId, null, null, null, 10000, cancellationToken);
    }

    public async Task<DependencyListResult> FileDependenciesAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var result = new DependencyListResult();
        var includes = new List<DependencyInfo>();

        if (!File.Exists(file))
        {
            throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");
        }

        var content = File.ReadAllText(file);

        var includeRegex = new Regex(@"^\s*#\s*include\s*[<""]([^>""]+)[>""]\s*$", RegexOptions.Multiline);

        foreach (Match match in includeRegex.Matches(content))
        {
            var includeFile = match.Groups[1].Value;
            var lineNum = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            var type = match.Value.Contains("<") ? "system" : "local";

            includes.Add(new DependencyInfo
            {
                File = includeFile,
                Line = lineNum,
                Type = type
            });
        }

        result.Includes = includes;
        result.Total = includes.Count;

        return result;
    }

    private async Task<VisualStudioWorkspace> GetWorkspaceAsync(CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        if (await _package.GetServiceAsync(typeof(SComponentModel)) is not IComponentModel cm)
            throw new VsmcpException(ErrorCodes.InteropFault, "IComponentModel unavailable.");
        var ws = cm.GetService<VisualStudioWorkspace>()
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "VisualStudioWorkspace unavailable.");
        return ws;
    }

    private static Document? FindDocument(Solution solution, string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var id = solution.GetDocumentIdsWithFilePath(full).FirstOrDefault();
        return id is null ? null : solution.GetDocument(id);
    }
}
