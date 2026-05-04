using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Wraps Workspace.TryApplyChanges with consistent error handling. Use this for any tool that
/// produces a Roslyn-aware mutation (rename, format, organize usings, code actions, etc.).
/// Edits go through the live VS workspace so they show up in open buffers and integrate with undo.
/// </summary>
internal static class RoslynEditor
{
    /// <summary>
    /// Apply a new solution to the workspace. Returns the list of files actually changed
    /// (path-deduplicated) so the caller can report verification feedback.
    /// </summary>
    public static IReadOnlyList<string> Apply(VisualStudioWorkspace workspace, Solution newSolution)
    {
        var oldSolution = workspace.CurrentSolution;
        var changedDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in newSolution.GetChanges(oldSolution).GetProjectChanges())
        {
            foreach (var docId in change.GetChangedDocuments())
            {
                var doc = newSolution.GetDocument(docId);
                if (!string.IsNullOrEmpty(doc?.FilePath)) changedDocs.Add(doc!.FilePath!);
            }
            foreach (var docId in change.GetAddedDocuments())
            {
                var doc = newSolution.GetDocument(docId);
                if (!string.IsNullOrEmpty(doc?.FilePath)) changedDocs.Add(doc!.FilePath!);
            }
        }

        if (!workspace.TryApplyChanges(newSolution))
            throw new VsmcpException(ErrorCodes.WrongState,
                "Workspace.TryApplyChanges returned false. " +
                "The workspace may have changed since the edit was computed, or a conflict was detected.");

        return changedDocs.ToList();
    }

    /// <summary>
    /// Replace the syntax root of a single document and apply.
    /// </summary>
    public static IReadOnlyList<string> ApplyDocumentChange(
        VisualStudioWorkspace workspace, Document doc, SyntaxNode newRoot)
    {
        var newDoc = doc.WithSyntaxRoot(newRoot);
        return Apply(workspace, newDoc.Project.Solution);
    }

    /// <summary>
    /// Replace the source text of a single document and apply.
    /// </summary>
    public static IReadOnlyList<string> ApplyTextChange(
        VisualStudioWorkspace workspace, Document doc, SourceText newText)
    {
        var newDoc = doc.WithText(newText);
        return Apply(workspace, newDoc.Project.Solution);
    }
}
