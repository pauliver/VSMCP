using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- #77 Verified mutations companion --------

    public async Task<GroupedDiagnosticsResult> CodeVerifyFilesAsync(
        IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0) return new GroupedDiagnosticsResult();
        var ws = await GetWorkspaceAsync(cancellationToken);
        var result = new GroupedDiagnosticsResult();

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = FindDocument(ws.CurrentSolution, f);
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
        return msg.Length <= 80 ? msg : msg.Substring(0, 79) + "…";
    }

    // -------- #78 + #80 Path-interned + cursor-paginated text search --------

    public async Task<TextSearchResult> SearchTextCompactAsync(
        string pattern, string? filePattern, string? projectId,
        IReadOnlyList<string>? kinds, int maxResults, string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new VsmcpException(ErrorCodes.NotFound, "pattern is required.");
        if (maxResults <= 0) maxResults = 500;

        // Decode the cursor: { "skip": N }.
        int skip = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor!));
                var m = Regex.Match(raw, @"""skip""\s*:\s*(\d+)");
                if (m.Success) skip = int.Parse(m.Groups[1].Value);
            }
            catch { skip = 0; }
        }

        // Drive the existing search; collect into a flat list, then page + intern.
        var raw2 = await SearchTextAsync(pattern, filePattern, projectId, kinds, 50_000, cancellationToken)
            .ConfigureAwait(false);
        var all = raw2.Matches;

        var page = all.Skip(skip).Take(maxResults).ToList();
        var pathToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pathTable = new Dictionary<int, string>();
        var compactMatches = new List<TextMatch>();
        foreach (var m in page)
        {
            if (!pathToId.TryGetValue(m.File, out var id))
            {
                id = pathTable.Count;
                pathToId[m.File] = id;
                pathTable[id] = m.File;
            }
            // Replace File with the interned ID encoded as a string.
            compactMatches.Add(new TextMatch
            {
                File = id.ToString(),
                Line = m.Line,
                Column = m.Column,
                LineText = m.LineText,
                ContextBefore = m.ContextBefore,
                ContextAfter = m.ContextAfter,
            });
        }

        var remaining = Math.Max(0, all.Count - (skip + page.Count));
        string? nextCursor = remaining > 0
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"skip\":{skip + page.Count}}}"))
            : null;

        return new TextSearchResult
        {
            Matches = compactMatches,
            Total = all.Count,
            Truncated = remaining > 0,
            PathTable = pathTable,
            NextCursor = nextCursor,
            RemainingCount = remaining,
        };
    }

    // -------- #84 Frame-interned diag events --------

    public async Task<DiagEventsResult> DiagEventsListInternedAsync(
        string? filter, int maxResults, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");

        // Get the standard list, then build a shared frame table.
        var raw = collector.GetEvents(filter, maxResults <= 0 ? 100 : maxResults);
        var framesTable = new Dictionary<int, StackFrameInfo>();
        var keyToId = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in raw.Events)
        {
            // Pull detail to access frames (DiagEvent doesn't carry them in list form).
            var detail = collector.GetDetail(e.Id);
            if (detail?.Frames is null) continue;
            var frameIds = new List<int>();
            foreach (var f in detail.Frames)
            {
                var key = $"{f.FunctionName}|{f.Module}|{f.File}|{f.Line}";
                if (!keyToId.TryGetValue(key, out var id))
                {
                    id = framesTable.Count;
                    keyToId[key] = id;
                    framesTable[id] = f;
                }
                frameIds.Add(id);
            }
            e.FrameIds = frameIds;
        }

        raw.FramesTable = framesTable;
        return raw;
    }

    // -------- #87 frame.locals_summary (top-level only) --------

    public async Task<VariableListResult> FrameLocalsSummaryAsync(
        int? threadId, int? frameIndex, CancellationToken cancellationToken = default)
    {
        // expandDepth=0 hits depth-1 only. Reuse existing FrameLocalsAsync; this is just a curated default.
        return await FrameLocalsAsync(threadId, frameIndex, expandDepth: 0, cancellationToken).ConfigureAwait(false);
    }
}
