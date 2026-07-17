using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the UTF-8-byte → UTF-16-char column conversion that makes cpp rename splices correct
/// on lines containing non-ASCII text (libclang columns are byte offsets).
/// </summary>
public class TextColumnsTests
{
    [Fact]
    public void Ascii_prefix_is_identity()
        => Assert.Equal(10, TextColumns.Utf8ByteToCharIndex("int value = Vec3(1);", 10));

    [Fact]
    public void Two_byte_char_shifts_offset()
    {
        // "// ö comment" — ö is 2 UTF-8 bytes at char index 3, so byte offset 5 == char index 4.
        var line = "// ö x";
        Assert.Equal(4, TextColumns.Utf8ByteToCharIndex(line, 5));
    }

    [Fact]
    public void Three_byte_char_shifts_offset()
    {
        // "→x": → (U+2192) is 3 UTF-8 bytes, so x sits at byte offset 3, char index 1.
        Assert.Equal(1, TextColumns.Utf8ByteToCharIndex("→x", 3));
    }

    [Fact]
    public void Surrogate_pair_counts_four_bytes()
    {
        // "😀x": U+1F600 is 4 UTF-8 bytes / 2 UTF-16 chars, so x = byte offset 4, char index 2.
        Assert.Equal(2, TextColumns.Utf8ByteToCharIndex("\U0001F600x", 4));
    }

    [Fact]
    public void Offset_inside_multibyte_char_is_rejected()
        => Assert.Equal(-1, TextColumns.Utf8ByteToCharIndex("öx", 1));

    [Fact]
    public void Offset_at_end_of_line_returns_length()
        => Assert.Equal(3, TextColumns.Utf8ByteToCharIndex("abc", 3));

    [Fact]
    public void Offset_past_end_is_rejected()
        => Assert.Equal(-1, TextColumns.Utf8ByteToCharIndex("abc", 4));

    [Fact]
    public void Negative_offset_is_rejected()
        => Assert.Equal(-1, TextColumns.Utf8ByteToCharIndex("abc", -1));
}
