using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix
{
    internal sealed partial class RpcTarget
    {
        public async Task<GroupedDiagnosticsResult> CodeDiagnosticsGroupedAsync(string? file, int maxResults, CancellationToken cancellationToken = default)
        {
            var raw = await CodeDiagnosticsAsync(file, maxResults <= 0 ? 1000 : maxResults, cancellationToken).ConfigureAwait(false);
            var grouped = CtxHelpers.GroupDiagnostics(raw.Diagnostics);
            grouped.Truncated = raw.Truncated;
            return grouped;
        }
public async Task<GroupedDiagnosticsResult> CodeVerifyFilesAsync(
        IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0) return new GroupedDiagnosticsResult();
        var ws = await GetWorkspaceAsync(cancellationToken);
        var result = new GroupedDiagnosticsResult();

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = FindDocumentAnywhere(ws.CurrentSolution, f);
            if (doc is null) continue;
            var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (sm is null) continue;
            foreach (var d in sm.GetDiagnostics(cancellationToken: cancellationToken))
            {
                var sev = d.Severity switch
                {
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Error => CodeDiagnosticSeverity.Error,
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => CodeDiagnosticSeverity.Warning,
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Info => CodeDiagnosticSeverity.Info,
                    _ => CodeDiagnosticSeverity.Hidden,
                };
                if (sev != CodeDiagnosticSeverity.Error && sev != CodeDiagnosticSeverity.Warning) continue;

                var loc = d.Location.GetLineSpan();
                var path = loc.Path ?? f;
                if (!result.Files.TryGetValue(path, out var bucket))
                    result.Files[path] = bucket = new FileDiagnostics();

                var compact = new CompactDiagnostic
                {
                    Id = d.Id,
                    Severity = sev,
                    Line = loc.StartLinePosition.Line + 1,
                    Column = loc.StartLinePosition.Character + 1,
                    Identifier = ExtractFirstQuoted(d.GetMessage()),
                    MessageBrief = TrimMsg(d.GetMessage()),
                };
                if (sev == CodeDiagnosticSeverity.Error) { bucket.Errors.Add(compact); result.TotalErrors++; }
                else { bucket.Warnings.Add(compact); result.TotalWarnings++; }
            }
        }
        return result;
    }
private static readonly Regex s_quoted = new(@"'([^']+)'", RegexOptions.Compiled);
private static string? ExtractFirstQuoted(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return null;
        var m = s_quoted.Match(msg);
        return m.Success ? m.Groups[1].Value : null;
    }
private static string? TrimMsg(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return null;
        return msg.Length <= 80 ? msg : msg.Substring(0, 79) + "â€¦";
    }
    }
}