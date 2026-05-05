using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace VSMCP.Vsix;

/// <summary>
/// In-process AdhocWorkspace that loads loose csprojs from disk so Roslyn-aware tools have
/// project context in VS Open Folder mode (where the live VisualStudioWorkspace only sees
/// MiscFiles). Syntactic operations (FindDocument, member walks, outline) work end-to-end;
/// semantic operations (diagnostics, find references, quick info) are best-effort and will
/// be limited until BCL/NuGet metadata references are wired up — see follow-up.
/// </summary>
internal sealed class WorkspaceSidecar : IDisposable
{
    private readonly AdhocWorkspace _ws = new();
    private readonly Dictionary<string, ProjectId> _projectsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DocumentId> _docsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".vs", ".git", "node_modules", "packages", "Build", "out", "Release", "Debug",
    };

    public Workspace Workspace => _ws;

    /// <summary>Look up a document by its absolute path. Null if no project contains it.</summary>
    public Document? FindDocument(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        lock (_lock)
        {
            return _docsByPath.TryGetValue(full, out var id)
                ? _ws.CurrentSolution.GetDocument(id)
                : null;
        }
    }

    public IEnumerable<string> LoadedProjectPaths()
    {
        lock (_lock) return _projectsByPath.Keys.ToArray();
    }

    public int LoadedDocumentCount()
    {
        lock (_lock) return _docsByPath.Count;
    }

    /// <summary>
    /// Load (or reload) a single .csproj into the sidecar. Globs *.cs under the csproj's
    /// directory, skipping bin/obj/.vs/etc, and adds them as Documents.
    /// </summary>
    public LoadProjectOutcome LoadProject(string csprojPath)
    {
        var full = Path.GetFullPath(csprojPath);
        if (!File.Exists(full))
            return new LoadProjectOutcome { Path = full, Loaded = false, Error = "csproj not found" };

        var projectName = Path.GetFileNameWithoutExtension(full);
        var projectDir = Path.GetDirectoryName(full)!;
        var defaultNamespace = projectName;

        // Try to read RootNamespace / AssemblyName from the csproj for better defaults.
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(full);
            var ns = doc.Root?.Name.NamespaceName ?? "";
            var rootNs = doc.Descendants(string.IsNullOrEmpty(ns) ? "RootNamespace" : System.Xml.Linq.XName.Get("RootNamespace", ns))
                .FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(rootNs)) defaultNamespace = rootNs!;
        }
        catch { /* best-effort */ }

        ProjectId projectId;
        bool reload = false;
        lock (_lock)
        {
            if (_projectsByPath.TryGetValue(full, out var existing))
            {
                projectId = existing;
                reload = true;
                // Drop any existing documents for this project from our maps.
                var staleDocs = _docsByPath.Where(kv =>
                    _ws.CurrentSolution.GetProject(projectId)?.DocumentIds.Contains(kv.Value) ?? false)
                    .Select(kv => kv.Key).ToList();
                foreach (var k in staleDocs) _docsByPath.Remove(k);

                // Remove and re-add the project to clear its documents.
                if (!_ws.TryApplyChanges(_ws.CurrentSolution.RemoveProject(projectId)))
                {
                    return new LoadProjectOutcome { Path = full, Loaded = false, Error = "failed to remove existing project for reload" };
                }
            }
            projectId = ProjectId.CreateNewId(debugName: projectName);
            _projectsByPath[full] = projectId;

            var projectInfo = ProjectInfo.Create(
                id: projectId,
                version: VersionStamp.Create(),
                name: projectName,
                assemblyName: projectName,
                language: LanguageNames.CSharp,
                filePath: full,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: CSharpParseOptions.Default);

            if (!_ws.TryApplyChanges(_ws.CurrentSolution.AddProject(projectInfo).WithProjectDefaultNamespace(projectId, defaultNamespace)))
            {
                _projectsByPath.Remove(full);
                return new LoadProjectOutcome { Path = full, Loaded = false, Error = "failed to add project to sidecar workspace" };
            }
        }

        // Glob .cs files under the project directory.
        var docsAdded = 0;
        foreach (var csFile in EnumerateCsFiles(projectDir))
        {
            try
            {
                var content = File.ReadAllText(csFile);
                var docId = DocumentId.CreateNewId(projectId);
                var docInfo = DocumentInfo.Create(
                    id: docId,
                    name: Path.GetFileName(csFile),
                    folders: GetFolders(projectDir, csFile),
                    sourceCodeKind: SourceCodeKind.Regular,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(content), VersionStamp.Create(), csFile)),
                    filePath: csFile);

                lock (_lock)
                {
                    if (_ws.TryApplyChanges(_ws.CurrentSolution.AddDocument(docInfo)))
                    {
                        _docsByPath[csFile] = docId;
                        docsAdded++;
                    }
                }
            }
            catch
            {
                // Skip files we can't read; report total count below.
            }
        }

        return new LoadProjectOutcome
        {
            Path = full,
            Loaded = true,
            Reloaded = reload,
            ProjectName = projectName,
            DocumentsAdded = docsAdded,
        };
    }

    /// <summary>Recursively scan a folder for .csproj and load each.</summary>
    public LoadFolderOutcome LoadFolder(string rootPath)
    {
        var outcome = new LoadFolderOutcome { Root = Path.GetFullPath(rootPath) };
        if (!Directory.Exists(outcome.Root))
        {
            outcome.Error = "directory not found";
            return outcome;
        }

        foreach (var csproj in EnumerateCsprojs(outcome.Root))
        {
            outcome.Projects.Add(LoadProject(csproj));
        }
        outcome.TotalDocumentsAdded = outcome.Projects.Sum(p => p.DocumentsAdded);
        return outcome;
    }

    private static IEnumerable<string> EnumerateCsprojs(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] csprojs;
            try { csprojs = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly); }
            catch { csprojs = Array.Empty<string>(); }
            foreach (var c in csprojs) yield return c;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { subs = Array.Empty<string>(); }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (SkipDirs.Contains(name)) continue;
                stack.Push(s);
            }
        }
    }

    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] csFiles;
            try { csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly); }
            catch { csFiles = Array.Empty<string>(); }
            foreach (var f in csFiles) yield return f;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { subs = Array.Empty<string>(); }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (SkipDirs.Contains(name)) continue;
                stack.Push(s);
            }
        }
    }

    private static IEnumerable<string> GetFolders(string projectDir, string csFile)
    {
        var rel = Path.GetDirectoryName(csFile)!;
        if (string.IsNullOrEmpty(rel) || string.Equals(rel, projectDir, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        if (rel.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
            rel = rel.Substring(projectDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Length == 0
            ? Array.Empty<string>()
            : rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
    }

    public void Dispose() => _ws.Dispose();
}

internal sealed class LoadProjectOutcome
{
    public string Path { get; set; } = "";
    public bool Loaded { get; set; }
    public bool Reloaded { get; set; }
    public string? ProjectName { get; set; }
    public int DocumentsAdded { get; set; }
    public string? Error { get; set; }
}

internal sealed class LoadFolderOutcome
{
    public string Root { get; set; } = "";
    public List<LoadProjectOutcome> Projects { get; set; } = new();
    public int TotalDocumentsAdded { get; set; }
    public string? Error { get; set; }
}
