using System;
using System.Collections.Generic;
using System.Linq;

namespace VSMCP.Core;

/// <summary>
/// A single hunk of a unified diff: a contiguous run of context (' '), removed ('-'), and
/// added ('+') lines anchored at <see cref="OldStart"/> in the original file (1-based).
/// </summary>
public sealed class DiffHunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    /// <summary>Raw hunk body lines, each still carrying its leading ' ', '-' or '+'.</summary>
    public List<string> Lines { get; set; } = new();
}

/// <summary>All hunks targeting one file, with the paths from its <c>---</c>/<c>+++</c> headers.</summary>
public sealed class FilePatch
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public List<DiffHunk> Hunks { get; set; } = new();
}

/// <summary>Outcome of applying one <see cref="FilePatch"/> to file text.</summary>
public sealed class PatchApplyResult
{
    public bool Success { get; set; }
    public string? NewText { get; set; }
    public int HunksApplied { get; set; }
    public int HunksFailed { get; set; }
    public List<string> Failures { get; set; } = new();
}

/// <summary>
/// Minimal unified-diff parser + applier (#137), pure and unit-testable. Supports multi-file
/// patches, multiple hunks per file, and a small fuzz window so a patch still applies when line
/// numbers have drifted slightly. Not git-grade (no rename/binary/mode handling), but enough for
/// an agent to hand VSMCP a diff and have it applied through the editor.
/// </summary>
public static class UnifiedDiff
{
    /// <summary>Parse a unified diff into per-file patches. Lenient about leading junk.</summary>
    public static List<FilePatch> Parse(string diff)
    {
        var patches = new List<FilePatch>();
        if (string.IsNullOrEmpty(diff)) return patches;

        var lines = diff.Replace("\r\n", "\n").Split('\n');
        FilePatch? current = null;
        DiffHunk? hunk = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                current = new FilePatch { OldPath = StripPathPrefix(line.Substring(4).Trim()) };
                hunk = null;
                // The matching +++ header should be the next line.
                if (i + 1 < lines.Length && lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
                {
                    current.NewPath = StripPathPrefix(lines[i + 1].Substring(4).Trim());
                    i++;
                }
                patches.Add(current);
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (current is null) { current = new FilePatch(); patches.Add(current); }
                hunk = ParseHunkHeader(line);
                if (hunk is not null) current.Hunks.Add(hunk);
                continue;
            }

            if (hunk is not null && line.Length > 0 && (line[0] == ' ' || line[0] == '+' || line[0] == '-'))
            {
                hunk.Lines.Add(line);
                continue;
            }
            // A bare "" is the trailing split artifact or inter-section blank, not a hunk line
            // (a blank context line is " "). "\ No newline at end of file" markers: also ignored.
        }
        return patches;
    }

    /// <summary>
    /// Apply a file's hunks to <paramref name="original"/>. Returns the new text on success;
    /// on any hunk mismatch the file is left unchanged and the failures are reported.
    /// </summary>
    public static PatchApplyResult Apply(string original, FilePatch patch, int fuzz = 2)
    {
        var result = new PatchApplyResult();
        var src = original.Replace("\r\n", "\n").Split('\n').ToList();
        bool trailingNewline = original.EndsWith("\n");

        // Apply hunks top-to-bottom, tracking the running line offset as edits change length.
        int offset = 0;
        foreach (var h in patch.Hunks)
        {
            var oldBlock = h.Lines.Where(l => l[0] == ' ' || l[0] == '-').Select(l => l.Substring(1)).ToList();
            var newBlock = h.Lines.Where(l => l[0] == ' ' || l[0] == '+').Select(l => l.Substring(1)).ToList();

            int guess = h.OldStart - 1 + offset;        // 0-based expected position
            int at = Locate(src, oldBlock, guess, fuzz);
            if (at < 0)
            {
                result.HunksFailed++;
                result.Failures.Add($"hunk @@ -{h.OldStart},{h.OldCount} +{h.NewStart},{h.NewCount} @@ did not match");
                continue;
            }
            src.RemoveRange(at, oldBlock.Count);
            src.InsertRange(at, newBlock);
            offset += newBlock.Count - oldBlock.Count;
            result.HunksApplied++;
        }

        if (result.HunksFailed > 0)
        {
            result.Success = false;
            return result;
        }

        var joined = string.Join("\n", src);
        if (trailingNewline && !joined.EndsWith("\n")) joined += "\n";
        result.NewText = joined;
        result.Success = true;
        return result;
    }

    // Find oldBlock in src at/near guess. Exact match at guess first, then widen by +/- fuzz.
    private static int Locate(List<string> src, List<string> oldBlock, int guess, int fuzz)
    {
        if (oldBlock.Count == 0) return Math.Max(0, Math.Min(guess, src.Count));
        if (Matches(src, oldBlock, guess)) return guess;
        for (int d = 1; d <= fuzz; d++)
        {
            if (Matches(src, oldBlock, guess - d)) return guess - d;
            if (Matches(src, oldBlock, guess + d)) return guess + d;
        }
        return -1;
    }

    private static bool Matches(List<string> src, List<string> block, int at)
    {
        if (at < 0 || at + block.Count > src.Count) return false;
        for (int k = 0; k < block.Count; k++)
            if (src[at + k] != block[k]) return false;
        return true;
    }

    private static DiffHunk? ParseHunkHeader(string line)
    {
        // @@ -oldStart,oldCount +newStart,newCount @@ [section]
        int at1 = line.IndexOf('-');
        int plus = line.IndexOf('+');
        if (at1 < 0 || plus < 0) return null;
        var (os, oc) = ParseRange(line, at1 + 1);
        var (ns, nc) = ParseRange(line, plus + 1);
        if (os < 0 || ns < 0) return null;
        return new DiffHunk { OldStart = os, OldCount = oc, NewStart = ns, NewCount = nc };
    }

    private static (int start, int count) ParseRange(string line, int from)
    {
        int i = from;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i == from) return (-1, 0);
        int start = int.Parse(line.Substring(from, i - from));
        int count = 1;
        if (i < line.Length && line[i] == ',')
        {
            int j = i + 1;
            while (j < line.Length && char.IsDigit(line[j])) j++;
            count = int.Parse(line.Substring(i + 1, j - i - 1));
        }
        return (start, count);
    }

    private static string StripPathPrefix(string path)
    {
        // Drop a trailing tab+timestamp if present, then a leading a/ or b/ git prefix.
        int tab = path.IndexOf('\t');
        if (tab >= 0) path = path.Substring(0, tab);
        if (path.Length > 2 && (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal)))
            path = path.Substring(2);
        return path;
    }
}
