using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Helpers shared by the context-efficiency tools (#72-#89).
/// </summary>
internal static class CtxHelpers
{
    private static readonly Regex s_idQuoteRx = new(@"'([^']+)'", RegexOptions.Compiled);

    public static string Sha1Hex(string text)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string Sha1Hex(byte[] bytes)
    {
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string DetectEncoding(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[4];
            int n = fs.Read(head, 0, 4);
            if (n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return "utf-8-bom";
            if (n >= 2 && head[0] == 0xFF && head[1] == 0xFE) return "utf-16-le";
            if (n >= 2 && head[0] == 0xFE && head[1] == 0xFF) return "utf-16-be";
            return "utf-8";
        }
        catch { return "utf-8"; }
    }

    public static CompactDiagnostic Compact(CodeDiagnosticInfo d)
    {
        var ident = d.Message is null ? null : s_idQuoteRx.Match(d.Message).Groups[1].Value;
        return new CompactDiagnostic
        {
            Id = d.Id,
            Severity = d.Severity,
            Line = d.Location?.StartLine ?? 0,
            Column = d.Location?.StartColumn ?? 0,
            Identifier = string.IsNullOrEmpty(ident) ? null : ident,
            MessageBrief = string.IsNullOrEmpty(d.Message)
                ? null
                : (d.Message.Length <= 80 ? d.Message : d.Message.Substring(0, 79) + "…"),
        };
    }

    public static CompactDiagnostic Compact(BuildDiagnostic d, CodeDiagnosticSeverity sev)
    {
        var ident = d.Message is null ? null : s_idQuoteRx.Match(d.Message).Groups[1].Value;
        return new CompactDiagnostic
        {
            Id = d.Code ?? "",
            Severity = sev,
            Line = d.Line ?? 0,
            Column = d.Column ?? 0,
            Identifier = string.IsNullOrEmpty(ident) ? null : ident,
            MessageBrief = string.IsNullOrEmpty(d.Message)
                ? null
                : (d.Message.Length <= 80 ? d.Message : d.Message.Substring(0, 79) + "…"),
        };
    }

    public static GroupedDiagnosticsResult GroupDiagnostics(IEnumerable<CodeDiagnosticInfo> diags)
    {
        var result = new GroupedDiagnosticsResult();
        foreach (var d in diags)
        {
            var path = d.Location?.File ?? "<unknown>";
            if (!result.Files.TryGetValue(path, out var bucket))
            {
                bucket = new FileDiagnostics();
                result.Files[path] = bucket;
            }
            var c = Compact(d);
            if (d.Severity == CodeDiagnosticSeverity.Error)
            {
                bucket.Errors.Add(c);
                result.TotalErrors++;
            }
            else if (d.Severity == CodeDiagnosticSeverity.Warning)
            {
                bucket.Warnings.Add(c);
                result.TotalWarnings++;
            }
        }
        return result;
    }

    public static GroupedDiagnosticsResult GroupBuildDiagnostics(
        IEnumerable<BuildDiagnostic> errors, IEnumerable<BuildDiagnostic> warnings)
    {
        var result = new GroupedDiagnosticsResult();
        foreach (var d in errors)
        {
            var path = d.File ?? "<unknown>";
            if (!result.Files.TryGetValue(path, out var bucket))
                result.Files[path] = bucket = new FileDiagnostics();
            bucket.Errors.Add(Compact(d, CodeDiagnosticSeverity.Error));
            result.TotalErrors++;
        }
        foreach (var d in warnings)
        {
            var path = d.File ?? "<unknown>";
            if (!result.Files.TryGetValue(path, out var bucket))
                result.Files[path] = bucket = new FileDiagnostics();
            bucket.Warnings.Add(Compact(d, CodeDiagnosticSeverity.Warning));
            result.TotalWarnings++;
        }
        return result;
    }

    /// <summary>
    /// Roslyn SymbolDisplayFormat for the requested compactness level (#79).
    /// </summary>
    public static SymbolDisplayFormat ToFormat(SymbolDisplayMode mode) => mode switch
    {
        SymbolDisplayMode.Minimal => new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType),
        SymbolDisplayMode.Full => SymbolDisplayFormat.FullyQualifiedFormat,
        _ => SymbolDisplayFormat.MinimallyQualifiedFormat,
    };

    /// <summary>
    /// Best-effort line-based diff with 0 lines of context. Used by code.diff (#88) when
    /// neither a git checkout nor a stored prior version is available — pure text diff
    /// against an explicit baseline string.
    /// </summary>
    public static List<DiffHunk> TextDiff(string from, string to)
    {
        var fromLines = from.Replace("\r\n", "\n").Split('\n');
        var toLines = to.Replace("\r\n", "\n").Split('\n');
        var hunks = new List<DiffHunk>();

        int i = 0, j = 0;
        while (i < fromLines.Length && j < toLines.Length)
        {
            if (fromLines[i] == toLines[j]) { i++; j++; continue; }

            var hunk = new DiffHunk { StartLine = j + 1 };
            int origI = i, origJ = j;

            // Find next syncing line within a small window (greedy LCS-lite).
            int sync = FindNextSync(fromLines, toLines, i, j, window: 200);
            if (sync < 0)
            {
                // Diverged for the rest — dump tails as a single hunk.
                while (i < fromLines.Length) hunk.RemovedLines.Add(fromLines[i++]);
                while (j < toLines.Length) hunk.AddedLines.Add(toLines[j++]);
                hunks.Add(hunk);
                break;
            }
            int newI = origI + (sync >> 16);
            int newJ = origJ + (sync & 0xFFFF);
            for (int k = origI; k < newI; k++) hunk.RemovedLines.Add(fromLines[k]);
            for (int k = origJ; k < newJ; k++) hunk.AddedLines.Add(toLines[k]);
            hunks.Add(hunk);
            i = newI;
            j = newJ;
        }
        if (i < fromLines.Length || j < toLines.Length)
        {
            var tail = new DiffHunk { StartLine = j + 1 };
            while (i < fromLines.Length) tail.RemovedLines.Add(fromLines[i++]);
            while (j < toLines.Length) tail.AddedLines.Add(toLines[j++]);
            if (tail.RemovedLines.Count + tail.AddedLines.Count > 0) hunks.Add(tail);
        }
        return hunks;
    }

    private static int FindNextSync(string[] a, string[] b, int ai, int bi, int window)
    {
        int maxA = Math.Min(a.Length, ai + window);
        int maxB = Math.Min(b.Length, bi + window);
        for (int da = 0; da < maxA - ai; da++)
        {
            for (int db = 0; db < maxB - bi; db++)
            {
                if (da == 0 && db == 0) continue;
                if (a[ai + da] == b[bi + db]) return (da << 16) | db;
            }
        }
        return -1;
    }
}

/// <summary>
/// Per-RPC-target session scope for #82. Held by RpcTarget and consulted by symbol-resolution tools.
/// </summary>
internal sealed class SessionScope
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public List<string> Symbols { get; set; } = new();
    public string? Project { get; set; }
    public string? Folder { get; set; }
    public long EstablishedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
