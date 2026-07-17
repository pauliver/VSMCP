namespace VSMCP.Core;

/// <summary>
/// Column conversions between libclang's UTF-8 byte offsets and .NET's UTF-16 char indices.
/// libclang reports (line, column) with column as a 1-based BYTE offset into the UTF-8 line;
/// indexing a C# string with it goes wrong as soon as any non-ASCII character precedes the
/// target — which silently broke rename splices on such lines.
/// </summary>
public static class TextColumns
{
    /// <summary>
    /// Convert a 0-based UTF-8 byte offset within <paramref name="line"/> to the 0-based char
    /// index. For pure-ASCII prefixes this is the identity. Returns -1 when the offset is out of
    /// range or lands inside a multi-byte sequence (a sign the offset doesn't belong to this text).
    /// </summary>
    public static int Utf8ByteToCharIndex(string line, int byteOffset)
    {
        if (line is null || byteOffset < 0) return -1;

        int bytes = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (bytes == byteOffset) return i;

            char c = line[i];
            if (c < 0x80) bytes += 1;
            else if (c < 0x800) bytes += 2;
            else if (char.IsHighSurrogate(c) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
            {
                bytes += 4;
                i++; // consume the low surrogate — offsets inside the pair are mid-char
            }
            else bytes += 3;

            if (bytes > byteOffset) return -1; // landed inside a multi-byte character
        }
        return bytes == byteOffset ? line.Length : -1;
    }
}
