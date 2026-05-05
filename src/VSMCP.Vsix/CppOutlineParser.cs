using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Regex/state-machine outline scanner for C/C++ files. Not a real parser — handles the
/// common cases (namespaces, classes, structs, enums, functions, typedefs, using-aliases)
/// well enough that an LLM can navigate a header file without reading every line.
/// Skips line + block comments and preprocessor directives. Tracks brace depth and a
/// namespace stack so declarations are tagged with their containing scope.
/// </summary>
internal static class CppOutlineParser
{
    private static readonly Regex RxNamespace = new(
        @"^\s*namespace\s+(?<name>[A-Za-z_][\w:]*(?:::[A-Za-z_]\w*)*)\s*\{?",
        RegexOptions.Compiled);

    // No terminator required — class headers can span multiple lines (`class Foo : public Bar`
    // followed by `{` on the next line). The pending-class state machine in Parse() decides
    // when to push class scope based on the actual `{` location.
    private static readonly Regex RxClassStructUnion = new(
        @"^\s*(?:template\s*<[^>]+>\s*)?(?:export\s+)?(?<kind>class|struct|union)\s+(?:[A-Z_]+_API\s+)?(?<name>[A-Za-z_]\w*)\b(?![A-Za-z_0-9])",
        RegexOptions.Compiled);

    private static readonly Regex RxEnum = new(
        @"^\s*(?:template\s*<[^>]+>\s*)?enum\s+(?:class\s+|struct\s+)?(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.Compiled);

    private static readonly Regex RxTypedef = new(
        @"^\s*typedef\s+.+?\s+(?<name>[A-Za-z_]\w*)\s*[;\[]",
        RegexOptions.Compiled);

    private static readonly Regex RxUsingAlias = new(
        @"^\s*using\s+(?<name>[A-Za-z_]\w*)\s*=",
        RegexOptions.Compiled);

    // Function declaration / definition: return type + name + parens + opt trailing modifiers
    // (const, noexcept, override, final, = default, = delete, = 0). Trailing modifiers can
    // appear in sequence separated by whitespace (e.g. `const noexcept`); the wrapper-and-star
    // pattern allows zero or more.
    private static readonly Regex RxFunction = new(
        @"^\s*(?:template\s*<[^>]+>\s*)?(?<sig>(?:inline\s+|static\s+|virtual\s+|constexpr\s+|explicit\s+|friend\s+|extern(?:\s*""[^""]*"")?\s+|noexcept\s+|\[\[[^\]]+\]\]\s+)*[\w:<>,\s\*\&]+?\s+(?<name>[A-Za-z_]\w*)\s*\([^)]*\)(?:\s+(?:const|noexcept|override|final|throw\s*\([^)]*\))|\s*=\s*(?:default|delete|0))*)\s*[{;]",
        RegexOptions.Compiled);

    public static CppOutlineResult Parse(string filePath, int maxDecls = 5000)
    {
        var result = new CppOutlineResult { File = filePath };
        if (!File.Exists(filePath))
            throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var nsStack = new Stack<(string name, int braceDepthAtEnter)>();
        var classStack = new Stack<(string kind, string name, int braceDepthAtEnter)>();
        int braceDepth = 0;
        bool inBlockComment = false;

        // Pending class state — set when a class header is detected without an open brace
        // on the same line. Resolved when the next line shows a `{` (push to classStack)
        // or `;` (forward declaration; drop pending).
        string? pendingClassKind = null;
        string? pendingClassName = null;
        int pendingClassDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (result.Declarations.Count >= maxDecls)
            {
                result.Truncated = true;
                break;
            }

            var rawLine = lines[i];
            var line = StripComments(rawLine, ref inBlockComment);

            // Resolve pending class against this line BEFORE matching new declarations.
            if (pendingClassName is not null)
            {
                int openIdx = line.IndexOf('{');
                int closeIdx = line.IndexOf(';');
                if (openIdx >= 0 && (closeIdx < 0 || openIdx < closeIdx))
                {
                    classStack.Push((pendingClassKind!, pendingClassName, pendingClassDepth));
                    pendingClassKind = pendingClassName = null;
                }
                else if (closeIdx >= 0)
                {
                    pendingClassKind = pendingClassName = null;
                }
            }

            if (string.IsNullOrWhiteSpace(line)) { CountBraces(line, ref braceDepth); PopScopes(braceDepth, nsStack, classStack); continue; }
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                // Preprocessor — ignore but still count visible braces (rare in #directives).
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }

            // Match in priority order. Early-return on the first hit per line.
            if (TryMatchNamespace(line, i + 1, nsStack, braceDepth, result, out var namespaceConsumed))
            {
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }
            // Class/struct/union match — inlined here so we can update the pending-class state
            // when the opening brace is on a later line.
            {
                var classMatch = RxClassStructUnion.Match(line);
                if (classMatch.Success)
                {
                    var kind = classMatch.Groups["kind"].Value;
                    var name = classMatch.Groups["name"].Value;
                    result.Declarations.Add(new CppDecl
                    {
                        Kind = kind,
                        Name = name,
                        Container = ContainerString(nsStack, classStack),
                        Line = i + 1,
                        Signature = line.Trim(),
                    });
                    // Decide if this is a definition (`{` on this line, after the match), forward
                    // decl (`;` next), or pending (header continues to a later line).
                    int restStart = classMatch.Index + classMatch.Length;
                    int openIdx = line.IndexOf('{', restStart);
                    int closeIdx = line.IndexOf(';', restStart);
                    if (openIdx >= 0 && (closeIdx < 0 || openIdx < closeIdx))
                    {
                        classStack.Push((kind, name, braceDepth));
                    }
                    else if (closeIdx >= 0)
                    {
                        // Forward decl; do nothing.
                    }
                    else
                    {
                        // Pending — opener will appear on a later line.
                        pendingClassKind = kind;
                        pendingClassName = name;
                        pendingClassDepth = braceDepth;
                    }
                    CountBraces(line, ref braceDepth);
                    PopScopes(braceDepth, nsStack, classStack);
                    continue;
                }
            }
            if (TryMatchEnum(line, i + 1, ContainerString(nsStack, classStack), result))
            {
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }
            if (TryMatchTypedef(line, i + 1, ContainerString(nsStack, classStack), result))
            {
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }
            if (TryMatchUsingAlias(line, i + 1, ContainerString(nsStack, classStack), result))
            {
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }
            if (TryMatchFunction(line, i + 1, ContainerString(nsStack, classStack), braceDepth, classStack, nsStack, result))
            {
                CountBraces(line, ref braceDepth);
                PopScopes(braceDepth, nsStack, classStack);
                continue;
            }

            CountBraces(line, ref braceDepth);
            PopScopes(braceDepth, nsStack, classStack);
        }

        result.Total = result.Declarations.Count;
        return result;
    }

    private static string StripComments(string line, ref bool inBlock)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            if (inBlock)
            {
                if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '/')
                {
                    inBlock = false;
                    i += 2;
                }
                else { i++; }
            }
            else
            {
                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/') { break; }
                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                {
                    inBlock = true;
                    i += 2;
                }
                else
                {
                    sb.Append(line[i]);
                    i++;
                }
            }
        }
        return sb.ToString();
    }

    private static void CountBraces(string line, ref int depth)
    {
        // Naive: count { and } outside of strings / chars. Strings are rare in declarations
        // and getting them perfectly right needs a real lexer; skip for simplicity.
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
    }

    private static void PopScopes(int depth, Stack<(string name, int braceDepthAtEnter)> ns, Stack<(string kind, string name, int braceDepthAtEnter)> cls)
    {
        while (cls.Count > 0 && cls.Peek().braceDepthAtEnter >= depth) cls.Pop();
        while (ns.Count > 0 && ns.Peek().braceDepthAtEnter >= depth) ns.Pop();
    }

    private static string? ContainerString(Stack<(string name, int braceDepthAtEnter)> ns, Stack<(string kind, string name, int braceDepthAtEnter)> cls)
    {
        // Stacks iterate top-first. To produce a path "Outer::Inner::Class", insert namespaces
        // at index 0 (so bottom-of-stack lands first) and append classes after the namespaces.
        var parts = new List<string>();
        foreach (var n in ns) parts.Insert(0, n.name);
        var clsList = new List<string>();
        foreach (var c in cls) clsList.Insert(0, c.name);
        parts.AddRange(clsList);
        if (parts.Count == 0) return null;
        return string.Join("::", parts);
    }

    private static bool TryMatchNamespace(string line, int lineNumber, Stack<(string name, int braceDepthAtEnter)> ns, int braceDepthBefore, CppOutlineResult result, out bool consumed)
    {
        consumed = false;
        var m = RxNamespace.Match(line);
        if (!m.Success) return false;
        var name = m.Groups["name"].Value;
        var hasOpenBrace = line.IndexOf('{') >= 0;
        result.Declarations.Add(new CppDecl
        {
            Kind = "namespace",
            Name = name,
            Container = ns.Count == 0 ? null : ContainerStringFromNs(ns),
            Line = lineNumber,
            Signature = line.Trim(),
        });
        if (hasOpenBrace)
        {
            // Each `{` that opens a namespace needs to map to brace depth. We push at
            // pre-increment depth so PopScopes triggers when depth drops back to that level.
            ns.Push((name, braceDepthBefore));
        }
        consumed = true;
        return true;
    }

    private static string? ContainerStringFromNs(Stack<(string name, int braceDepthAtEnter)> ns)
    {
        if (ns.Count == 0) return null;
        var parts = new List<string>();
        foreach (var n in ns) parts.Insert(0, n.name);
        return string.Join("::", parts);
    }

    private static bool TryMatchClass(string line, int lineNumber, string? container, Stack<(string kind, string name, int braceDepthAtEnter)> cls, int braceDepthBefore, CppOutlineResult result)
    {
        var m = RxClassStructUnion.Match(line);
        if (!m.Success) return false;
        var kind = m.Groups["kind"].Value;
        var name = m.Groups["name"].Value;
        var terminator = m.Groups["terminator"].Value;
        result.Declarations.Add(new CppDecl
        {
            Kind = kind,
            Name = name,
            Container = container,
            Line = lineNumber,
            Signature = line.Trim(),
        });
        if (terminator == "{")
        {
            cls.Push((kind, name, braceDepthBefore));
        }
        return true;
    }

    private static bool TryMatchEnum(string line, int lineNumber, string? container, CppOutlineResult result)
    {
        var m = RxEnum.Match(line);
        if (!m.Success) return false;
        result.Declarations.Add(new CppDecl
        {
            Kind = "enum",
            Name = m.Groups["name"].Value,
            Container = container,
            Line = lineNumber,
            Signature = line.Trim(),
        });
        return true;
    }

    private static bool TryMatchTypedef(string line, int lineNumber, string? container, CppOutlineResult result)
    {
        var m = RxTypedef.Match(line);
        if (!m.Success) return false;
        result.Declarations.Add(new CppDecl
        {
            Kind = "typedef",
            Name = m.Groups["name"].Value,
            Container = container,
            Line = lineNumber,
            Signature = line.Trim(),
        });
        return true;
    }

    private static bool TryMatchUsingAlias(string line, int lineNumber, string? container, CppOutlineResult result)
    {
        var m = RxUsingAlias.Match(line);
        if (!m.Success) return false;
        result.Declarations.Add(new CppDecl
        {
            Kind = "using",
            Name = m.Groups["name"].Value,
            Container = container,
            Line = lineNumber,
            Signature = line.Trim(),
        });
        return true;
    }

    private static bool TryMatchFunction(string line, int lineNumber, string? container, int braceDepth, Stack<(string kind, string name, int braceDepthAtEnter)> cls, Stack<(string name, int braceDepthAtEnter)> ns, CppOutlineResult result)
    {
        // Inside a class/struct (depth > namespace count) the function is a member; outside, it's free.
        var m = RxFunction.Match(line);
        if (!m.Success) return false;

        var name = m.Groups["name"].Value;
        // Filter out matches that are clearly not functions (e.g. variable initializations
        // mistaken by the regex). Ignore matches whose "name" is a common keyword.
        if (IsKeyword(name)) return false;

        var kind = cls.Count > 0 ? "method" : "function";
        result.Declarations.Add(new CppDecl
        {
            Kind = kind,
            Name = name,
            Container = container,
            Line = lineNumber,
            Signature = m.Groups["sig"].Value.Trim(),
        });
        return true;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "switch", "return", "case", "do", "else", "sizeof",
        "operator", "throw", "catch", "try", "const_cast", "dynamic_cast", "static_cast",
        "reinterpret_cast",
    };
    private static bool IsKeyword(string name) => Keywords.Contains(name);
}
