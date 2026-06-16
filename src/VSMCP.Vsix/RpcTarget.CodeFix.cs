using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// List the Roslyn code fixes (lightbulb actions) available for the diagnostic(s) on the
    /// line at a position (#134). CodeFixProviders are imported from the VS MEF catalog.
    /// </summary>
    public async Task<ListFixesResult> CodeListFixesAsync(CodePosition position, CancellationToken cancellationToken = default)
    {
        var (doc, ws, actions) = await GatherFixesAsync(position, cancellationToken).ConfigureAwait(false);
        _ = doc; _ = ws;
        var result = new ListFixesResult();
        var seen = new HashSet<string>();
        foreach (var (diagId, action) in actions)
            if (seen.Add(action.Title))
                result.Fixes.Add(new CodeFixInfo { Title = action.Title, DiagnosticId = diagId });
        return result;
    }

    /// <summary>
    /// Apply a code fix at a position (#134). Matches by Title when given, else applies the
    /// first available fix. The fix's operations are applied through the workspace (VS undo).
    /// </summary>
    public async Task<ApplyFixResult> CodeApplyFixAsync(CodePosition position, string? title, CancellationToken cancellationToken = default)
    {
        var (doc, ws, actions) = await GatherFixesAsync(position, cancellationToken).ConfigureAwait(false);
        _ = doc;
        if (actions.Count == 0)
            return new ApplyFixResult { Applied = false, Error = "No code fixes are available at that position." };

        var chosen = title is null
            ? actions[0].action
            : actions.FirstOrDefault(a => string.Equals(a.action.Title, title, StringComparison.Ordinal)).action;
        if (chosen is null)
            return new ApplyFixResult { Applied = false, Error = $"No fix titled '{title}'. Call code.list_fixes to see options." };

        try
        {
            var ops = await chosen.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            // ApplyChangesOperation.Apply -> VisualStudioWorkspace.TryApplyChanges requires the
            // UI thread; GetOperationsAsync used ConfigureAwait(false), so re-marshal first.
            await _jtf.SwitchToMainThreadAsync(cancellationToken);
            foreach (var op in ops)
                op.Apply(ws, cancellationToken);
            return new ApplyFixResult { Applied = true, AppliedTitle = chosen.Title };
        }
        catch (Exception ex)
        {
            VsmcpLog.Debug("code.apply_fix", $"apply failed: {chosen.Title}", ex);
            return new ApplyFixResult { Applied = false, AppliedTitle = chosen.Title, Error = ex.Message };
        }
    }

    private async Task<(Document doc, Workspace ws, List<(string diagId, CodeAction action)> actions)> GatherFixesAsync(
        CodePosition position, CancellationToken cancellationToken)
    {
        if (position is null) throw new VsmcpException(ErrorCodes.NotFound, "position is required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(SComponentModel)) is not IComponentModel cm)
            throw new VsmcpException(ErrorCodes.InteropFault, "IComponentModel unavailable.");
        var ws = cm.GetService<VisualStudioWorkspace>()
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "VisualStudioWorkspace unavailable.");
        var doc = FindDocument(ws.CurrentSolution, position.File)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not part of any loaded project: {position.File}");

        var text = await doc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var pos = PositionFromLineCol(text, position.Line, position.Column);
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new VsmcpException(ErrorCodes.RoslynUnavailable, "No semantic model for the document.");
        var line = text.Lines.GetLineFromPosition(Math.Min(pos, Math.Max(0, text.Length - 1)));

        var diags = sm.GetDiagnostics(cancellationToken: cancellationToken)
            .Where(d => d.Location.IsInSource && d.Location.SourceSpan.IntersectsWith(line.Span))
            .ToList();

        var providers = cm.GetExtensions<CodeFixProvider>().ToList();
        var actions = new List<(string diagId, CodeAction action)>();
        foreach (var diag in diags)
        {
            foreach (var provider in providers)
            {
                if (!provider.FixableDiagnosticIds.Contains(diag.Id)) continue;
                var ctx = new CodeFixContext(doc, diag,
                    (action, _) => actions.Add((diag.Id, action)), cancellationToken);
                try { await provider.RegisterCodeFixesAsync(ctx).ConfigureAwait(false); }
                catch (Exception ex) { VsmcpLog.Debug("code.list_fixes", $"provider {provider.GetType().Name} threw", ex); }
            }
        }
        return (doc, ws, actions);
    }
}
