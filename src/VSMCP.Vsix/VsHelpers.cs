using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Free-floating helpers that must run on the UI thread. Callers are responsible for the switch.
/// Throws <see cref="VsmcpException"/> with protocol-level error codes for non-fatal misuse.
/// </summary>
internal static class VsHelpers
{
    /// <summary>Recursively walk the solution and return every concrete (non-folder) project.</summary>
    public static IEnumerable<EnvDTE.Project> EnumerateProjects(EnvDTE.Solution? solution)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (solution is null) yield break;

        foreach (EnvDTE.Project p in solution.Projects)
        {
            if (p is null) continue;
            foreach (var inner in Flatten(p))
                yield return inner;
        }
    }

    private static IEnumerable<EnvDTE.Project> Flatten(EnvDTE.Project p)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.Equals(p.Kind, EnvDTE.Constants.vsProjectKindSolutionItems, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Kind, "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}", StringComparison.OrdinalIgnoreCase)) // solution folder
        {
            if (p.ProjectItems is null) yield break;
            foreach (EnvDTE.ProjectItem item in p.ProjectItems)
            {
                var sub = item?.SubProject;
                if (sub is null) continue;
                foreach (var inner in Flatten(sub))
                    yield return inner;
            }
            yield break;
        }

        yield return p;
    }

    public static EnvDTE.Project RequireProject(EnvDTE.Solution? solution, string projectId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var p in EnumerateProjects(solution))
        {
            if (string.Equals(p.UniqueName, projectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, projectId, StringComparison.OrdinalIgnoreCase))
                return p;

            string? full = null;
            try { full = p.FullName; } catch { }
            if (!string.IsNullOrEmpty(full) && string.Equals(full, projectId, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        throw new VsmcpException(ErrorCodes.NotFound, $"No project matching id '{projectId}' in the current solution.");
    }

    public static ProjectInfo ToInfo(EnvDTE.Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? fullPath = null;
        try { fullPath = project.FullName; } catch { }

        string? outputType = null;
        string? tfm = null;
        try { outputType = project.Properties?.Item("OutputType")?.Value?.ToString(); } catch { }
        try { tfm = project.Properties?.Item("TargetFramework")?.Value?.ToString(); } catch { }

        return new ProjectInfo
        {
            Id = project.UniqueName ?? project.Name ?? "",
            Name = project.Name ?? "",
            Kind = project.Kind,
            FullPath = fullPath,
            OutputType = outputType,
            TargetFramework = tfm,
        };
    }

    public static EnvDTE.ProjectItem? FindItem(EnvDTE.Project project, string relativeOrFullPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string target = Path.IsPathRooted(relativeOrFullPath)
            ? relativeOrFullPath
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullName) ?? "", relativeOrFullPath));

        return FindItemRecursive(project.ProjectItems, target);
    }

    private static EnvDTE.ProjectItem? FindItemRecursive(EnvDTE.ProjectItems? items, string fullPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (items is null) return null;

        foreach (EnvDTE.ProjectItem item in items)
        {
            if (item is null) continue;
            for (short i = 1; i <= item.FileCount; i++)
            {
                string? file = null;
                try { file = item.FileNames[i]; } catch { }
                if (!string.IsNullOrEmpty(file) && string.Equals(file, fullPath, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            var nested = FindItemRecursive(item.ProjectItems, fullPath);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>Get the open text buffer for a file, or null if the file isn't open.</summary>
    public static Microsoft.VisualStudio.Text.ITextBuffer? TryGetOpenTextBuffer(IServiceProvider sp, string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrEmpty(filePath)) return null;

        var rdt = sp.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
        if (rdt is null) return null;

        var hr = rdt.FindAndLockDocument(
            (uint)_VSRDTFLAGS.RDT_NoLock,
            filePath,
            out _, out _, out _,
            out var docCookie);
        if (hr != VSConstants.S_OK || docCookie == 0) return null;

        rdt.GetDocumentInfo(docCookie, out _, out _, out _, out _, out _, out _, out var docData);
        if (docData == IntPtr.Zero) return null;

        try
        {
            var dataObj = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(docData);
            if (dataObj is IVsTextBuffer vsBuffer)
            {
                var adapters = sp.GetService(typeof(SComponentModel)) as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;
                var factory = adapters?.GetService<IVsEditorAdaptersFactoryService>();
                return factory?.GetDataBuffer(vsBuffer);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.Release(docData);
        }
        return null;
    }

    /// <summary>Translate a 1-based <see cref="FileRange"/> into a 0-based (start, length) pair over raw text.</summary>
    public static (int Start, int Length) ToOffsets(string text, FileRange range)
    {
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') lineStarts.Add(i + 1);

        int LineLen(int idx)
        {
            int s = lineStarts[idx];
            int e = idx + 1 < lineStarts.Count ? lineStarts[idx + 1] : text.Length;
            int len = e - s;
            if (len > 0 && text[s + len - 1] == '\n') { len--; if (len > 0 && text[s + len - 1] == '\r') len--; }
            return len;
        }

        int startLine = Math.Max(1, range.StartLine) - 1;
        int endLine = Math.Max(startLine + 1, range.EndLine) - 1;
        if (startLine >= lineStarts.Count) startLine = lineStarts.Count - 1;
        if (endLine >= lineStarts.Count) endLine = lineStarts.Count - 1;

        int startCol = Math.Min(LineLen(startLine), Math.Max(1, range.StartColumn) - 1);
        int endCol = Math.Min(LineLen(endLine), Math.Max(1, range.EndColumn) - 1);

        int start = lineStarts[startLine] + startCol;
        int end = lineStarts[endLine] + endCol;
        if (end < start) end = start;
        return (start, end - start);
    }

    /// <summary>Translate a 1-based <see cref="FileRange"/> into a 0-based <see cref="Microsoft.VisualStudio.Text.Span"/> against a snapshot.</summary>
    public static Microsoft.VisualStudio.Text.Span ToSpan(Microsoft.VisualStudio.Text.ITextSnapshot snapshot, FileRange range)
    {
        int startLine = Math.Max(1, range.StartLine) - 1;
        int endLine = Math.Max(startLine + 1, range.EndLine) - 1;
        if (startLine >= snapshot.LineCount) startLine = snapshot.LineCount - 1;
        if (endLine >= snapshot.LineCount) endLine = snapshot.LineCount - 1;

        var startLineObj = snapshot.GetLineFromLineNumber(startLine);
        var endLineObj = snapshot.GetLineFromLineNumber(endLine);

        int startCol = Math.Min(startLineObj.Length, Math.Max(1, range.StartColumn) - 1);
        int endCol = Math.Min(endLineObj.Length, Math.Max(1, range.EndColumn) - 1);

        int start = startLineObj.Start.Position + startCol;
        int end = endLineObj.Start.Position + endCol;
        if (end < start) end = start;
        return Microsoft.VisualStudio.Text.Span.FromBounds(start, end);
    }
}

internal sealed class VsmcpException : Exception
{
    public string Code { get; }
    public VsmcpException(string code, string message) : base($"{code}: {message}") => Code = code;
    public VsmcpException(string code, string message, Exception inner) : base($"{code}: {message}", inner) => Code = code;
}
