namespace VSMCP.Core;

/// <summary>
/// Finds where a C++ member/type declaration ends by balancing braces — but over the parser's
/// <see cref="CppOutlineParser.Sanitize"/>d text, so braces inside string / char literals and
/// <c>/* */</c> block comments don't corrupt the count. The old in-VSIX counter handled only
/// <c>//</c> comments and, when it never balanced, returned <c>startLine + 50</c> — an arbitrary line
/// that made cpp_replace_member / cpp_move_* delete or replace the wrong span silently.
///
/// Pure and in Core so it is unit-testable; returns -1 when the braces never balance so the caller
/// can fail loudly instead of guessing a range.
/// </summary>
public static class CppMemberBoundary
{
    /// <summary>0-based index of the line that closes the member starting at <paramref name="startLineIdx"/>, or -1 if unbalanced.</summary>
    public static int FindEndLine(string[] lines, int startLineIdx)
    {
        if (lines is null || startLineIdx < 0 || startLineIdx >= lines.Length) return -1;

        var lex = new CppOutlineParser.CppLexState();
        int depth = 0;
        bool sawOpen = false;

        for (int i = startLineIdx; i < lines.Length; i++)
        {
            var line = CppOutlineParser.Sanitize(lines[i], ref lex);
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (ch == '{') { depth++; sawOpen = true; }
                else if (ch == '}')
                {
                    depth--;
                    if (sawOpen && depth == 0) return i;
                }
                else if (ch == ';' && !sawOpen)
                {
                    return i; // forward declaration / field with no body
                }
            }
        }
        return -1;
    }
}
