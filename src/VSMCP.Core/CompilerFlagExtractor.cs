using System;
using System.Collections.Generic;
using System.Text;

namespace VSMCP.Core;

/// <summary>Include/define/std flags extracted from a compiler invocation.</summary>
public sealed class CompilerFlags
{
    public List<string> Includes { get; } = new();        // -I / /I
    public List<string> SystemIncludes { get; } = new();  // -isystem / -external:I
    public List<string> Defines { get; } = new();         // -D / /D (kept whole, e.g. FOO=1)
    public string? Std { get; set; }                       // c++17 (from -std= / /std:)
}

/// <summary>
/// Parses include/define/std flags out of a C/C++ compile command (#135). Pure and
/// dependency-free so it's unit-testable; the JSON reading of compile_commands.json lives in
/// the Server where System.Text.Json is available. Handles both clang/gcc (-I, -isystem, -D,
/// -std=) and MSVC (/I, /D, /std:) spellings, attached or space-separated.
/// </summary>
public static class CompilerFlagExtractor
{
    /// <summary>Split a command string into argv, respecting double quotes.</summary>
    public static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(command)) return tokens;
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (var c in command)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    public static CompilerFlags Extract(IReadOnlyList<string> args)
    {
        var flags = new CompilerFlags();
        if (args is null) return flags;

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Count ? args[++i] : null;

            if (a == "-isystem" || a == "-external:I") { var v = Next(); if (v != null) flags.SystemIncludes.Add(v); }
            else if (a.StartsWith("-isystem", StringComparison.Ordinal) && a.Length > 8) flags.SystemIncludes.Add(a.Substring(8));
            else if (a == "-I" || a == "/I") { var v = Next(); if (v != null) flags.Includes.Add(v); }
            else if ((a.StartsWith("-I", StringComparison.Ordinal) || a.StartsWith("/I", StringComparison.Ordinal)) && a.Length > 2)
                flags.Includes.Add(a.Substring(2));
            else if (a == "-D" || a == "/D") { var v = Next(); if (v != null) flags.Defines.Add(v); }
            else if ((a.StartsWith("-D", StringComparison.Ordinal) || a.StartsWith("/D", StringComparison.Ordinal)) && a.Length > 2)
                flags.Defines.Add(a.Substring(2));
            else if (a.StartsWith("-std=", StringComparison.Ordinal)) flags.Std = a.Substring(5);
            else if (a.StartsWith("/std:", StringComparison.Ordinal)) flags.Std = a.Substring(5);
        }
        return flags;
    }

    /// <summary>Convenience: tokenize a command string then extract flags.</summary>
    public static CompilerFlags ExtractFromCommand(string command) => Extract(Tokenize(command));
}
