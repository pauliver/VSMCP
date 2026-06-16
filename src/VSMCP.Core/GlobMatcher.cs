using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VSMCP.Core;

/// <summary>
/// Filesystem glob matching extracted from RpcTarget.FileDiscovery so it is unit-testable without
/// VS. Segment semantics: <c>*</c> within a segment, <c>**</c> across segments, <c>?</c> a single
/// non-separator char, <c>{a,b}</c> alternation. Case-insensitive; the produced regex is anchored
/// (<c>^...$</c>).
///
/// <see cref="MatchesPathOrName"/> tests the pattern against the full path OR the bare file name,
/// so a pattern like <c>*.cs</c> (no <c>**/</c>) matches files in subdirectories. This mirrors the
/// behavior file.glob already had; file.list and search.text previously matched the full path only
/// and so returned nothing for bare patterns like <c>*.cs</c> or <c>Foo.cs</c>.
/// </summary>
public static class GlobMatcher
{
    public static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
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

    /// <summary>Match a glob against the full path OR the bare file name (so "*.cs" matches "a/b/x.cs").</summary>
    public static bool MatchesPathOrName(string path, string pattern)
    {
        var rx = ToRegex(pattern);
        return rx.IsMatch(path) || rx.IsMatch(Path.GetFileName(path));
    }
}
