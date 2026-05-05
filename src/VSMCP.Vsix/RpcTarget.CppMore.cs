using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // ---- cpp_outline_many ----

    public async Task<CppOutlineManyResult> CppOutlineManyAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        if (files is null) throw new VsmcpException(ErrorCodes.NotFound, "files is required.");
        await Task.Yield();
        var result = new CppOutlineManyResult();
        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new CppOutlineEntry { File = f };
            try { entry.Outline = CppOutlineParser.Parse(f); }
            catch (Exception ex) { entry.Error = ex.Message; }
            result.Entries.Add(entry);
        }
        return result;
    }

    // ---- cpp_inheritance ----

    // Matches across newlines so multi-line class declarations work:
    //   class Sample : public SampleBase
    //   {
    private static readonly Regex s_inheritanceRx = new(
        @"\b(?<kind>class|struct)\s+(?<name>[A-Za-z_]\w*)\s*(?:\:\s*(?<bases>[^{;]+))?\s*\{",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public async Task<CppInheritanceResult> CppInheritanceAsync(string file, string className, int maxDepth, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (maxDepth <= 0) maxDepth = 4;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var tree = await BuildInheritanceTreeAsync(file, className, maxDepth, visited, cancellationToken).ConfigureAwait(false);
        return new CppInheritanceResult { ClassName = className, Tree = tree };
    }

    private async Task<CppInheritanceNode?> BuildInheritanceTreeAsync(string? file, string className, int depthRemaining, HashSet<string> visited, CancellationToken ct)
    {
        if (depthRemaining < 0 || !visited.Add(className)) return null;
        var node = new CppInheritanceNode { Name = className };
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return node;

        node.File = file;
        // Find the class declaration + parse its inheritance list. The regex spans
        // newlines (Singleline mode) so multi-line declarations like
        //   class Foo : public Bar
        //   {
        // are matched.
        string text;
        try { text = File.ReadAllText(file!); } catch { return node; }

        foreach (Match m in s_inheritanceRx.Matches(text))
        {
            if (m.Groups["name"].Value != className) continue;
            // Convert offset to 1-based line number.
            int line = 1;
            for (int i = 0; i < m.Index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            node.Line = line;
            var basesStr = m.Groups["bases"].Value.Trim();
            if (string.IsNullOrEmpty(basesStr)) break;
            foreach (var b in ParseBaseList(basesStr))
            {
                ct.ThrowIfCancellationRequested();
                // Try to find the base class declaration via cpp_find_symbol (solution-wide).
                string? baseFile = null;
                try
                {
                    var find = await CppFindSymbolAsync(b.Name, "class", 1, ct).ConfigureAwait(false);
                    if (find.Matches.Count == 0)
                        find = await CppFindSymbolAsync(b.Name, "struct", 1, ct).ConfigureAwait(false);
                    baseFile = find.Matches.FirstOrDefault()?.File;
                }
                catch { /* find failure is non-fatal */ }
                var baseNode = await BuildInheritanceTreeAsync(baseFile, b.Name, depthRemaining - 1, visited, ct).ConfigureAwait(false);
                if (baseNode is null) baseNode = new CppInheritanceNode { Name = b.Name };
                baseNode.Access = b.Access;
                node.Bases.Add(baseNode);
            }
            break;
        }
        return node;
    }

    private static IEnumerable<(string Access, string Name)> ParseBaseList(string bases)
    {
        // "public Foo, private Bar, virtual public Baz<T>" — split on commas, strip access keywords.
        foreach (var raw in bases.Split(','))
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            string access = "private";
            foreach (var kw in new[] { "public", "private", "protected" })
            {
                if (Regex.IsMatch(s, $@"\b{kw}\b"))
                {
                    access = kw;
                    s = Regex.Replace(s, $@"\b{kw}\b", "").Trim();
                }
            }
            s = Regex.Replace(s, @"\bvirtual\b", "").Trim();
            // Strip template args for the name lookup.
            var lt = s.IndexOf('<');
            var name = lt > 0 ? s.Substring(0, lt).Trim() : s;
            // Strip namespace qualifications for the lookup name (e.g. "Z::Detail::Foo" → "Foo").
            var qq = name.LastIndexOf("::", StringComparison.Ordinal);
            if (qq >= 0) name = name.Substring(qq + 2);
            if (string.IsNullOrEmpty(name)) continue;
            yield return (access, name);
        }
    }

    // ---- cpp_generate_equality ----

    public async Task<CppGenerateEqualityResult> CppGenerateEqualityAsync(string file, string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        await Task.Yield();

        var outline = CppOutlineParser.Parse(file);
        var classDecl = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct") && d.Name == className);
        if (classDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Class/struct '{className}' not found in '{file}'.");

        var lines = File.ReadAllLines(file);
        int classStartIdx = classDecl.Line - 1;
        int classEndIdx = FindMemberEndLine(lines, classStartIdx);

        var fields = ExtractFieldNames(lines, classStartIdx + 1, classEndIdx);
        if (fields.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, $"No member fields found in '{className}' to compare.");

        var sb = new StringBuilder();
        sb.Append("    bool operator==(const ").Append(className).Append("& other) const noexcept").AppendLine();
        sb.AppendLine("    {");
        sb.Append("        return ");
        sb.Append(string.Join(" && ", fields.Select(f => $"{f} == other.{f}")));
        sb.AppendLine(";");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    bool operator!=(const ").Append(className).Append("& other) const noexcept { return !(*this == other); }").AppendLine();

        var code = sb.ToString();
        await FileReplaceRangeAsync(file, new FileRange
        {
            StartLine = classEndIdx + 1,
            StartColumn = 1,
            EndLine = classEndIdx + 1,
            EndColumn = 1,
        }, code, cancellationToken).ConfigureAwait(false);

        try { await CppInvalidateAsync(file, cancellationToken).ConfigureAwait(false); } catch { }

        return new CppGenerateEqualityResult
        {
            File = file,
            ClassName = className,
            Inserted = true,
            InsertedAtLine = classEndIdx + 1,
            Code = code.TrimEnd('\r', '\n'),
            FieldsCompared = fields.Count,
        };
    }

    private static List<string> ExtractFieldNames(string[] lines, int firstLineIdx, int lastLineIdx)
    {
        var fieldRx = new Regex(@"^\s*(?:[\w:<>,\s\*\&]+?)\s+(?<name>[A-Za-z_]\w*)\s*(?:=\s*[^;]+)?;\s*(?:\/\/.*)?$",
            RegexOptions.Compiled);
        var fields = new List<string>();
        for (int i = firstLineIdx; i < lastLineIdx; i++)
        {
            if (i >= lines.Length) break;
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;
            if (trimmed.StartsWith("public:") || trimmed.StartsWith("private:") || trimmed.StartsWith("protected:")) continue;
            if (trimmed.Contains("(")) continue;
            var m = fieldRx.Match(lines[i]);
            if (!m.Success) continue;
            fields.Add(m.Groups["name"].Value);
        }
        return fields;
    }

    // ---- cpp_implement_interface ----

    public async Task<CppImplementInterfaceResult> CppImplementInterfaceAsync(string derivedFile, string derivedClass, string baseClass, string? baseClassFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(derivedFile)) throw new VsmcpException(ErrorCodes.NotFound, "derivedFile is required.");
        if (string.IsNullOrEmpty(derivedClass)) throw new VsmcpException(ErrorCodes.NotFound, "derivedClass is required.");
        if (string.IsNullOrEmpty(baseClass)) throw new VsmcpException(ErrorCodes.NotFound, "baseClass is required.");

        // Strip template arguments from the base name for symbol lookup. "Base<T>" -> "Base".
        // The find_symbol path only knows declared names, not instantiations.
        var baseNameForLookup = baseClass;
        var lt = baseNameForLookup.IndexOf('<');
        if (lt > 0) baseNameForLookup = baseNameForLookup.Substring(0, lt).Trim();
        // Also strip namespace qualifiers — "ns::Foo<T>" -> "Foo".
        var qq = baseNameForLookup.LastIndexOf("::", StringComparison.Ordinal);
        if (qq >= 0) baseNameForLookup = baseNameForLookup.Substring(qq + 2);

        // Find baseClassFile if not supplied.
        if (string.IsNullOrEmpty(baseClassFile))
        {
            var find = await CppFindSymbolAsync(baseNameForLookup, "class", 1, cancellationToken).ConfigureAwait(false);
            if (find.Matches.Count == 0)
                find = await CppFindSymbolAsync(baseNameForLookup, "struct", 1, cancellationToken).ConfigureAwait(false);
            baseClassFile = find.Matches.FirstOrDefault()?.File;
            if (string.IsNullOrEmpty(baseClassFile))
                throw new VsmcpException(ErrorCodes.NotFound, $"Could not locate '{baseNameForLookup}' in solution.");
        }

        // Walk the base class for `virtual ... method(...)` declarations.
        var baseLines = File.ReadAllLines(baseClassFile!);
        var baseOutline = CppOutlineParser.Parse(baseClassFile!);
        var baseDecl = baseOutline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct") && d.Name == baseNameForLookup);
        if (baseDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Base class '{baseNameForLookup}' declaration not found in '{baseClassFile}'.");
        int baseStart = baseDecl.Line - 1;
        int baseEnd = FindMemberEndLine(baseLines, baseStart);

        // Pre-process: collapse multi-line virtual declarations into single lines so the
        // regex doesn't miss `virtual ret\n    method(args) const = 0;` patterns. Joins
        // until we see `;` or `{` outside template-bracket nesting.
        var collapsed = new List<string>();
        var sbLine = new StringBuilder();
        int templateDepth = 0;
        for (int i = baseStart + 1; i < baseEnd && i < baseLines.Length; i++)
        {
            foreach (var ch in baseLines[i])
            {
                if (ch == '<') templateDepth++;
                else if (ch == '>' && templateDepth > 0) templateDepth--;
            }
            if (sbLine.Length > 0) sbLine.Append(' ');
            sbLine.Append(baseLines[i].Trim());
            // Terminator outside template — end of statement.
            if (templateDepth == 0 && (baseLines[i].Contains(';') || baseLines[i].TrimEnd().EndsWith("{")))
            {
                collapsed.Add(sbLine.ToString());
                sbLine.Clear();
            }
        }
        if (sbLine.Length > 0) collapsed.Add(sbLine.ToString());

        // Allow multiple trailing modifiers (const, noexcept, override, final, throw(...))
        // before `= 0` or `;`. Also allow ref-qualifier `&` / `&&`.
        var virtualRx = new Regex(
            @"^\s*virtual\s+(?<sig>(?<ret>[\w:<>,\s\*\&]+?)\s+(?<name>[A-Za-z_]\w*)\s*\((?<params>[^)]*)\)" +
            @"\s*(?:&{1,2})?(?:\s+(?:const|noexcept|override|final|throw\s*\([^)]*\)))*)" +
            @"\s*(?:=\s*0)?\s*;",
            RegexOptions.Compiled);
        var virtuals = new List<(string Sig, string Name, string Ret, string Params)>();
        foreach (var line in collapsed)
        {
            var m = virtualRx.Match(line);
            if (!m.Success) continue;
            virtuals.Add((m.Groups["sig"].Value.Trim(), m.Groups["name"].Value, m.Groups["ret"].Value.Trim(), m.Groups["params"].Value));
        }

        if (virtuals.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, $"No virtual methods found in '{baseClass}'.");

        // Find the derived class.
        var derivedLines = File.ReadAllLines(derivedFile);
        var derivedOutline = CppOutlineParser.Parse(derivedFile);
        var derivedDecl = derivedOutline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct") && d.Name == derivedClass);
        if (derivedDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Derived class '{derivedClass}' not found in '{derivedFile}'.");
        int dStart = derivedDecl.Line - 1;
        int dEnd = FindMemberEndLine(derivedLines, dStart);

        // What's already overridden? Look at derived's member names.
        var alreadyOverridden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in derivedOutline.Declarations)
        {
            if (d.Kind != "method" && d.Kind != "function") continue;
            if (d.Container is null) continue;
            if (d.Container == derivedClass || d.Container.EndsWith("::" + derivedClass, StringComparison.Ordinal))
                alreadyOverridden.Add(d.Name);
        }

        var sb = new StringBuilder();
        var inserted = new List<string>();
        var skipped = new List<string>();
        foreach (var v in virtuals)
        {
            if (alreadyOverridden.Contains(v.Name)) { skipped.Add(v.Name); continue; }
            sb.Append("    ").Append(v.Ret).Append(' ').Append(v.Name).Append('(').Append(v.Params).Append(") override").AppendLine();
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: implement");
            sb.AppendLine("    }");
            sb.AppendLine();
            inserted.Add(v.Name);
        }

        if (inserted.Count == 0)
        {
            return new CppImplementInterfaceResult
            {
                DerivedFile = derivedFile,
                DerivedClass = derivedClass,
                BaseClass = baseClass,
                SkippedAlreadyOverridden = skipped,
                Code = "",
            };
        }

        var code = sb.ToString();
        await FileReplaceRangeAsync(derivedFile, new FileRange
        {
            StartLine = dEnd + 1,
            StartColumn = 1,
            EndLine = dEnd + 1,
            EndColumn = 1,
        }, code, cancellationToken).ConfigureAwait(false);

        try { await CppInvalidateAsync(derivedFile, cancellationToken).ConfigureAwait(false); } catch { }

        return new CppImplementInterfaceResult
        {
            DerivedFile = derivedFile,
            DerivedClass = derivedClass,
            BaseClass = baseClass,
            InsertedMethods = inserted,
            SkippedAlreadyOverridden = skipped,
            InsertedAtLine = dEnd + 1,
            Code = code.TrimEnd('\r', '\n'),
        };
    }

    // ---- cpp_scaffold_file ----

    public async Task<CppScaffoldFileResult> CppScaffoldFileAsync(string headerPath, string className, string? @namespace, bool createCpp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(headerPath)) throw new VsmcpException(ErrorCodes.NotFound, "headerPath is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (File.Exists(headerPath))
            throw new VsmcpException(ErrorCodes.WrongState, $"Header already exists: {headerPath}");
        await Task.Yield();

        var dir = Path.GetDirectoryName(headerPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var nl = "\r\n";
        var hdrName = Path.GetFileName(headerPath);

        var hdr = new StringBuilder();
        hdr.Append("#pragma once").Append(nl).Append(nl);
        if (!string.IsNullOrEmpty(@namespace))
        {
            hdr.Append("namespace ").Append(@namespace).Append(' ').Append('{').Append(nl).Append(nl);
        }
        hdr.Append("class ").Append(className).Append(nl);
        hdr.Append('{').Append(nl);
        hdr.Append("public:").Append(nl);
        hdr.Append("    ").Append(className).Append("();").Append(nl);
        hdr.Append("    ~").Append(className).Append("();").Append(nl);
        hdr.Append(nl);
        hdr.Append("private:").Append(nl);
        hdr.Append("    // members").Append(nl);
        hdr.Append("};").Append(nl);
        if (!string.IsNullOrEmpty(@namespace))
        {
            hdr.Append(nl).Append("} // namespace ").Append(@namespace).Append(nl);
        }

        File.WriteAllText(headerPath, hdr.ToString());

        var result = new CppScaffoldFileResult { HeaderPath = headerPath, Created = true };

        if (createCpp)
        {
            var cppPath = Path.ChangeExtension(headerPath, ".cpp");
            if (!File.Exists(cppPath))
            {
                var cpp = new StringBuilder();
                cpp.Append("#include \"").Append(hdrName).Append("\"").Append(nl).Append(nl);
                if (!string.IsNullOrEmpty(@namespace))
                {
                    cpp.Append("namespace ").Append(@namespace).Append(' ').Append('{').Append(nl).Append(nl);
                }
                cpp.Append(className).Append("::").Append(className).Append("()").Append(nl);
                cpp.Append("{").Append(nl);
                cpp.Append("}").Append(nl).Append(nl);
                cpp.Append(className).Append("::~").Append(className).Append("()").Append(nl);
                cpp.Append("{").Append(nl);
                cpp.Append("}").Append(nl);
                if (!string.IsNullOrEmpty(@namespace))
                {
                    cpp.Append(nl).Append("} // namespace ").Append(@namespace).Append(nl);
                }
                File.WriteAllText(cppPath, cpp.ToString());
                result.CppPath = cppPath;
            }
        }

        return result;
    }

    // ---- cpp_suggest_includes ----

    public async Task<CppSuggestIncludesResult> CppSuggestIncludesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");

        var find = await CppFindSymbolAsync(symbol, null, 20, cancellationToken).ConfigureAwait(false);
        var result = new CppSuggestIncludesResult { Symbol = symbol };

        // Prefer headers (.h/.hpp/.hxx) over .cpp/.cc, prefer declarations close to the top of the file.
        var ranked = find.Matches
            .Select(m => new { m, isHeader = IsHeader(m.File) })
            .OrderByDescending(x => x.isHeader)
            .ThenBy(x => x.m.Line)
            .Select(x => x.m)
            .Take(10)
            .ToList();

        foreach (var m in ranked)
        {
            result.Suggestions.Add(new CppIncludeSuggestion
            {
                Header = m.File,
                SymbolKind = m.Kind,
                Line = m.Line,
            });
        }

        return result;
    }

    private static bool IsHeader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".h" || ext == ".hpp" || ext == ".hxx" || ext == ".hh";
    }
}
