using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M12: File & Symbol Discovery --------

    public async Task<FileListResult> FileListAsync(
        string? projectId, string? folder, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var solution = dte.Solution;
        if (solution?.IsOpen != true)
            throw new VsmcpException(ErrorCodes.WrongState, "No solution is open.");

        if (maxResults <= 0) maxResults = 1000;
        if (maxResults > 50_000) maxResults = 50_000;

        var result = new FileListResult();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in VsHelpers.EnumerateProjects(solution))
        {
            if (projectId is not null && !ProjectIdMatches(project, projectId)) continue;

            cancellationToken.ThrowIfCancellationRequested();

            string? projectPath = null;
            try { projectPath = project.FullName; } catch { }
            if (string.IsNullOrEmpty(projectPath)) continue;
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";

            if (string.IsNullOrEmpty(folder))
            {
                CollectFromProjectItems(project.ProjectItems, projectDir, pattern, kinds, maxResults, result, seenPaths);
            }
            else
            {
                var folderPath = Path.Combine(projectDir, folder!.Replace('\\', '/'));
                CollectFilesFromFolder(project, folderPath, pattern, kinds, maxResults, result, seenPaths);
            }
            if (result.Files.Count >= maxResults) break;
        }

        result.Total = result.Files.Count;
        return result;
    }

    private static bool ProjectIdMatches(EnvDTE.Project project, string id)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return string.Equals(project.UniqueName, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.Name, id, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectFromProjectItems(
        EnvDTE.ProjectItems? items, string projectDir, string? pattern,
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

                result.Files.Add(new FileListItem
                {
                    Path = file,
                    Kind = kind,
                    Language = GetLanguage(item, file),
                    ProjectId = GetProjectId(item),
                });

                if (result.Files.Count >= maxResults)
                {
                    result.Truncated = true;
                    return;
                }
            }

            CollectFromProjectItems(item.ProjectItems, projectDir, pattern, kinds, maxResults, result, seenPaths);
            if (result.Files.Count >= maxResults) return;
        }
    }

    private static void CollectFilesFromFolder(
        EnvDTE.Project project, string folderPath, string? pattern,
        IReadOnlyList<string>? kinds, int maxResults, FileListResult result, HashSet<string> seenPaths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!Directory.Exists(folderPath)) return;

        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (!seenPaths.Add(file)) continue;
            const string kind = "file";
            if (kinds is not null && !kinds.Contains(kind)) continue;
            if (pattern is not null && !MatchesGlob(file, pattern)) continue;

            result.Files.Add(new FileListItem
            {
                Path = file,
                Kind = kind,
                Language = GetLanguage(file),
                ProjectId = project.UniqueName ?? project.Name,
            });

            if (result.Files.Count >= maxResults)
            {
                result.Truncated = true;
                return;
            }
        }
    }

    private static string GetItemKind(EnvDTE.ProjectItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { if (item.SubProject is not null) return "project"; } catch { }
        try
        {
            var k = item.Kind;
            if (string.Equals(k, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(k, EnvDTE.Constants.vsProjectItemKindVirtualFolder, StringComparison.OrdinalIgnoreCase))
                return "folder";
        }
        catch { }
        return "file";
    }

    private static string GetLanguage(EnvDTE.ProjectItem item, string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return GetLanguage(filePath);
    }

    private static string GetLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".cpp" or ".cc" or ".cxx" or ".c" => "cpp",
            ".h" or ".hpp" or ".hxx" => "cpp",
            ".vb" => "visualbasic",
            ".fs" or ".fsi" => "fsharp",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".jsx" => "javascript",
            ".xaml" => "xaml",
            ".xml" or ".csproj" or ".vbproj" or ".vcxproj" or ".sln" => "xml",
            ".json" => "json",
            ".md" => "markdown",
            _ => "unknown",
        };
    }

    private static string GetProjectId(EnvDTE.ProjectItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var p = item.ContainingProject;
            return p?.UniqueName ?? p?.Name ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Glob to regex with proper segment semantics: `*` within a segment, `**` across segments,
    /// `?` single non-separator char, `{a,b}` alternation. Case-insensitive.
    /// </summary>
    internal static bool MatchesGlob(string path, string pattern) => GlobToRegex(pattern).IsMatch(path);

    internal static Regex GlobToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder("^");
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    sb.Append(".*");
                    i += 2;
                    if (i < pattern.Length && (pattern[i] == '/' || pattern[i] == '\\')) i++;
                }
                else { sb.Append(@"[^/\\]*"); i++; }
            }
            else if (c == '?') { sb.Append(@"[^/\\]"); i++; }
            else if (c == '{')
            {
                int end = pattern.IndexOf('}', i);
                if (end < 0) { sb.Append(Regex.Escape("{")); i++; continue; }
                var alts = pattern.Substring(i + 1, end - i - 1).Split(',');
                sb.Append('(');
                for (int a = 0; a < alts.Length; a++)
                {
                    if (a > 0) sb.Append('|');
                    sb.Append(Regex.Escape(alts[a]));
                }
                sb.Append(')');
                i = end + 1;
            }
            else if (c == '/' || c == '\\') { sb.Append(@"[/\\]"); i++; }
            else { sb.Append(Regex.Escape(c.ToString())); i++; }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public async Task<FileListResult> FileGlobAsync(
        IReadOnlyList<string> patterns, string? projectId,
        CancellationToken cancellationToken = default)
    {
        if (patterns is null || patterns.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, "At least one pattern is required.");

        var all = await FileListAsync(projectId, null, null, new[] { "file" }, 50_000, cancellationToken);
        var compiled = patterns.Select(GlobToRegex).ToArray();
        var hits = new FileListResult();
        foreach (var f in all.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (compiled.Any(rx => rx.IsMatch(f.Path) || rx.IsMatch(Path.GetFileName(f.Path))))
                hits.Files.Add(f);
        }
        hits.Total = hits.Files.Count;
        return hits;
    }

    // -------- M12: Symbol discovery (Roslyn) --------

    public async Task<ClassesResult> FileClassesAsync(
        string? projectId, string? @namespace, IReadOnlyList<string>? kinds,
        int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0) maxResults = 1000;
        var ws = await GetWorkspaceAsync(cancellationToken);
        var result = new ClassesResult();

        foreach (var project in ws.CurrentSolution.Projects)
        {
            if (projectId is not null
                && !string.Equals(project.Name, projectId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(project.AssemblyName, projectId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var sm = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || sm is null) continue;

                foreach (var node in root.DescendantNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (node is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseNamespaceDeclarationSyntax))
                        continue;

                    var sym = sm.GetDeclaredSymbol(node);
                    if (sym is null) continue;
                    if (sym.Kind is not (SymbolKind.NamedType or SymbolKind.Namespace)) continue;

                    var entry = ToSymbolInfoLocal(sym);
                    if (kinds is not null && !kinds.Contains(entry.Kind)) continue;
                    if (@namespace is not null
                        && !string.Equals(entry.Container, @namespace, StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.Symbols.Add(entry);
                    if (result.Symbols.Count >= maxResults)
                    {
                        result.Truncated = true;
                        result.Total = result.Symbols.Count;
                        return result;
                    }
                }
            }
        }

        result.Total = result.Symbols.Count;
        return result;
    }

    internal static VSMCP.Shared.SymbolInfo ToSymbolInfoLocal(ISymbol symbol)
    {
        return new VSMCP.Shared.SymbolInfo
        {
            Name = symbol.Name,
            Kind = symbol.Kind.ToString().ToLowerInvariant(),
            Container = symbol.ContainingSymbol?.ToDisplayString(),
            Location = GetCodeSpan(symbol),
        };
    }

    internal static CodeSpan? GetCodeSpan(ISymbol symbol)
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
            EndColumn = span.EndLinePosition.Character + 1,
        };
    }

    public async Task<MembersResult> FileMembersAsync(
        string file, string className, IReadOnlyList<string>? kinds, bool excludeInherited,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrWhiteSpace(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var result = new MembersResult();
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || sm is null) return result;

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, className, StringComparison.Ordinal));
        if (typeDecl is null) return result;

        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (typeSym is null) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddMembers(typeSym, kinds, result, seen);

        if (!excludeInherited)
        {
            var b = typeSym.BaseType;
            while (b is not null && b.SpecialType != SpecialType.System_Object)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMembers(b, kinds, result, seen);
                b = b.BaseType;
            }
        }

        result.Total = result.Members.Count;
        return result;
    }

    private static void AddMembers(INamedTypeSymbol type, IReadOnlyList<string>? kinds, MembersResult result, HashSet<string> seen)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared) continue;
            var kind = member.Kind.ToString().ToLowerInvariant();
            if (kinds is not null && !kinds.Contains(kind)) continue;
            var sig = member.ToDisplayString();
            if (!seen.Add(sig)) continue;

            result.Members.Add(new MemberInfo
            {
                Name = member.Name,
                Kind = kind,
                Signature = sig,
                Location = GetCodeSpan(member),
                Access = member.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsStatic = member.IsStatic,
            });
        }
    }

    public async Task<InheritanceResult> FileInheritanceAsync(
        string file, string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrWhiteSpace(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || sm is null) return new InheritanceResult();

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, className, StringComparison.Ordinal));
        if (typeDecl is null) return new InheritanceResult();

        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (typeSym is null) return new InheritanceResult();

        var result = new InheritanceResult();

        var b = typeSym.BaseType;
        var basePath = new List<string> { typeSym.ToDisplayString() };
        while (b is not null && b.SpecialType != SpecialType.System_Object)
        {
            result.BaseTypes.Add(new InheritanceInfo { Name = b.ToDisplayString(), Location = GetCodeSpan(b) });
            basePath.Insert(0, b.ToDisplayString());
            b = b.BaseType;
        }

        foreach (var iface in typeSym.AllInterfaces)
            result.ImplementedInterfaces.Add(new InheritanceInfo { Name = iface.ToDisplayString(), Location = GetCodeSpan(iface) });

        try
        {
            if (typeSym.TypeKind == TypeKind.Interface)
            {
                var impls = await SymbolFinder.FindImplementationsAsync(
                    typeSym, ws.CurrentSolution, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var impl in impls)
                    result.DerivedTypes.Add(new InheritanceInfo { Name = impl.ToDisplayString(), Location = GetCodeSpan(impl) });
            }
            else
            {
                var derived = await SymbolFinder.FindDerivedClassesAsync(
                    typeSym, ws.CurrentSolution, transitive: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var d in derived)
                    result.DerivedTypes.Add(new InheritanceInfo { Name = d.ToDisplayString(), Location = GetCodeSpan(d) });
            }
        }
        catch { /* SymbolFinder can throw on partial workspaces */ }

        result.Hierarchy = new HierarchyInfo { Depth = basePath.Count - 1, Path = basePath };
        return result;
    }

    private static readonly Regex s_includeRx = new(
        @"^\s*#\s*include\s*(?:""(?<u>[^""]+)""|<(?<s>[^>]+)>)",
        RegexOptions.Compiled);

    public async Task<DependencyListResult> FileDependenciesAsync(
        string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await Task.Yield();

        var full = Path.GetFullPath(file);
        if (!File.Exists(full))
            throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {full}");

        var result = new DependencyListResult();
        string[] lines;
        try { lines = File.ReadAllLines(full); }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Read failed: {ex.Message}"); }

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var m = s_includeRx.Match(lines[i]);
            if (!m.Success) continue;
            var isSystem = m.Groups["s"].Success;
            var name = isSystem ? m.Groups["s"].Value : m.Groups["u"].Value;
            result.Includes.Add(new DependencyInfo
            {
                File = name,
                Line = i + 1,
                Type = isSystem ? "system" : "local",
            });
        }
        result.Total = result.Includes.Count;
        return result;
    }
}
