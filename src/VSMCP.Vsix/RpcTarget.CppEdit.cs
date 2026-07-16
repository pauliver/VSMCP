using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // ---- cpp_read_member ----

    public async Task<CppReadMemberResult> CppReadMemberAsync(string file, string className, string memberName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(memberName)) throw new VsmcpException(ErrorCodes.NotFound, "memberName is required.");
        await Task.Yield();

        var outline = CppOutlineParser.Parse(file);
        var hit = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "method" || d.Kind == "function")
            && d.Name == memberName
            && d.Container is not null
            && (d.Container == className || d.Container.EndsWith("::" + className, StringComparison.Ordinal)));
        if (hit is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Member '{className}::{memberName}' not found in '{file}'.");

        var lines = File.ReadAllLines(file);
        int startLineIdx = hit.Line - 1;
        int endLineIdx = FindMemberEndLine(lines, startLineIdx);

        var sb = new StringBuilder();
        for (int i = startLineIdx; i <= endLineIdx && i < lines.Length; i++)
        {
            sb.AppendLine(lines[i]);
        }

        if (Follow.Enabled)
            await Follow.TouchAsync(file, hit.Line, 1, isEdit: false, cancellationToken).ConfigureAwait(false);

        return new CppReadMemberResult
        {
            File = file,
            ClassName = className,
            MemberName = memberName,
            StartLine = hit.Line,
            EndLine = endLineIdx + 1,
            Content = sb.ToString().TrimEnd('\r', '\n'),
            Signature = hit.Signature,
        };
    }

    /// <summary>
    /// Line that closes the declaration starting at <paramref name="startLineIdx"/>. Delegates to the
    /// Core boundary scanner, which balances braces over Sanitize()d text (literals/comments stripped)
    /// so a <c>}</c> inside a string can't mis-close. Throws instead of guessing a range when the
    /// braces never balance — a wrong range silently corrupts the file.
    /// </summary>
    private static int FindMemberEndLine(string[] lines, int startLineIdx)
    {
        int end = VSMCP.Core.CppMemberBoundary.FindEndLine(lines, startLineIdx);
        if (end < 0)
            throw new VsmcpException(ErrorCodes.WrongState,
                "Could not determine the end of the C++ member (unbalanced braces). Refusing to guess a range to avoid corrupting the file.");
        return end;
    }

    // ---- cpp_analyzer_status ----

    public Task<CppAnalyzerStatusResult> CppAnalyzerStatusAsync(int recentLogLines, CancellationToken cancellationToken = default)
    {
        if (recentLogLines <= 0) recentLogLines = 50;
        var (proc, logPath, lastError, spawnAttempted) = CppAnalyzerHost.Probe();
        var result = new CppAnalyzerStatusResult
        {
            Spawned = spawnAttempted,
            ProcessId = proc?.Id,
            Alive = proc is not null && !proc.HasExited,
            ExitCode = proc is not null && proc.HasExited ? proc.ExitCode : null,
            LogPath = logPath,
            LastError = lastError,
        };
        result.RecentLog.AddRange(CppAnalyzerHost.SnapshotRecent(recentLogLines));
        return Task.FromResult(result);
    }

    // ---- cpp_organize_includes ----

    private static readonly Regex s_organizeIncludeRx = new(
        @"^\s*#\s*include\s*(?:""(?<u>[^""]+)""|<(?<s>[^>]+)>)\s*(?:\/\/.*)?$",
        RegexOptions.Compiled);

    public async Task<CppOrganizeIncludesResult> CppOrganizeIncludesAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");
        await Task.Yield();

        var lines = File.ReadAllLines(file).ToList();
        var nl = DetectLineEnding(file);

        // Find the include block: contiguous run of lines starting at the first #include
        // (with optional blank lines and comments in between). Stop at first non-include /
        // non-blank / non-comment line.
        int firstInclude = -1;
        int lastInclude = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (s_organizeIncludeRx.IsMatch(lines[i]))
            {
                if (firstInclude < 0) firstInclude = i;
                lastInclude = i;
            }
            else if (firstInclude >= 0)
            {
                // After we've started the block, keep collecting through blank lines + comments.
                var trimmed = lines[i].Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;
                break;
            }
        }

        if (firstInclude < 0)
            return new CppOrganizeIncludesResult { File = file, Changed = false, IncludesCounted = 0 };

        var system = new SortedSet<string>(StringComparer.Ordinal);
        var local = new SortedSet<string>(StringComparer.Ordinal);
        int total = 0, dupes = 0;
        for (int i = firstInclude; i <= lastInclude; i++)
        {
            var m = s_organizeIncludeRx.Match(lines[i]);
            if (!m.Success) continue;
            total++;
            if (m.Groups["s"].Success)
            {
                if (!system.Add(m.Groups["s"].Value)) dupes++;
            }
            else
            {
                if (!local.Add(m.Groups["u"].Value)) dupes++;
            }
        }

        var rebuilt = new StringBuilder();
        foreach (var s in system) rebuilt.Append("#include <").Append(s).Append('>').Append(nl);
        if (system.Count > 0 && local.Count > 0) rebuilt.Append(nl);
        foreach (var l in local) rebuilt.Append("#include \"").Append(l).Append('"').Append(nl);

        var newBlock = rebuilt.ToString().TrimEnd('\r', '\n');
        var oldBlock = string.Join(nl, lines.GetRange(firstInclude, lastInclude - firstInclude + 1));

        if (string.Equals(newBlock, oldBlock, StringComparison.Ordinal))
            return new CppOrganizeIncludesResult { File = file, Changed = false, IncludesCounted = total, Duplicates = dupes };

        // Replace via FileReplaceRangeAsync (1-based lines, full-line-cover columns).
        await FileReplaceRangeAsync(file, new FileRange
        {
            StartLine = firstInclude + 1,
            StartColumn = 1,
            EndLine = lastInclude + 1,
            EndColumn = lines[lastInclude].Length + 1,
        }, newBlock, cancellationToken).ConfigureAwait(false);

        return new CppOrganizeIncludesResult
        {
            File = file,
            Changed = true,
            IncludesCounted = total,
            Duplicates = dupes,
            Diff = $"--- old\n{oldBlock}\n+++ new\n{newBlock}",
        };
    }

    private static string DetectLineEnding(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            for (int i = 0; i < bytes.Length - 1; i++)
                if (bytes[i] == (byte)'\n')
                    return i > 0 && bytes[i - 1] == (byte)'\r' ? "\r\n" : "\n";
        }
        catch { }
        return "\r\n";
    }

    // ---- cpp_symbol_summary ----

    public async Task<CppSymbolSummaryResult> CppSymbolSummaryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");

        // Use cpp_find_symbol (regex-based) to discover candidate sites across the solution.
        var matches = await CppFindSymbolAsync(symbol, null, 50, cancellationToken).ConfigureAwait(false);
        var result = new CppSymbolSummaryResult { Symbol = symbol };

        // For each match, attempt to enrich with libclang quick-info (Type + BriefComment).
        // Best-effort — keep going if any one enrichment fails.
        foreach (var m in matches.Matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new CppSymbolSummaryEntry { Decl = m };
            try
            {
                var qi = await CppQuickInfoAsync(m.File, m.Line, EstimateColumn(m), null, null, cancellationToken).ConfigureAwait(false);
                entry.Type = qi.Type;
                entry.BriefComment = qi.BriefComment;
            }
            catch { /* enrichment failure is non-fatal */ }
            result.Entries.Add(entry);
        }
        result.Total = result.Entries.Count;
        return result;
    }

    public async Task<CppInvestigateResult> CppInvestigateAsync(string symbol, int maxRefs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");
        if (maxRefs <= 0) maxRefs = 50;

        // 1. Find the symbol via the syntactic search.
        var matches = await CppFindSymbolAsync(symbol, null, 1, cancellationToken).ConfigureAwait(false);
        var first = matches.Matches.FirstOrDefault();
        if (first is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbol}");

        var result = new CppInvestigateResult { Symbol = first };
        result.Stats.Kind = first.Kind;
        if (first.Signature.IndexOf("virtual", StringComparison.Ordinal) >= 0) result.Stats.IsVirtual = true;
        if (first.Signature.IndexOf("static", StringComparison.Ordinal) >= 0) result.Stats.IsStatic = true;
        if (first.Signature.IndexOf("const", StringComparison.Ordinal) >= 0) result.Stats.IsConst = true;

        // 2. Enrich with libclang quick-info (type + comment).
        try
        {
            var qi = await CppQuickInfoAsync(first.File, first.Line, EstimateColumn(first), null, null, cancellationToken).ConfigureAwait(false);
            result.QuickInfoType = qi.Type;
            result.BriefComment = qi.BriefComment;
            result.Stats.Type = qi.Type;
        }
        catch { /* enrichment failure is non-fatal */ }

        // 3. Read the body via cpp_read_member when the symbol lives inside a class/struct.
        if (!string.IsNullOrEmpty(first.Container) && (first.Kind == "method" || first.Kind == "function"))
        {
            try
            {
                var classNameSegment = first.Container!.Split(new[] { "::" }, StringSplitOptions.None).Last();
                var rm = await CppReadMemberAsync(first.File, classNameSegment, first.Name, cancellationToken).ConfigureAwait(false);
                result.Body = rm.Content;
            }
            catch { /* not a class member, fall through */ }
        }

        // 4. Solution-wide callers via cpp_find_references_solution.
        try
        {
            var refs = await CppFindReferencesSolutionAsync(first.File, first.Line, EstimateColumn(first),
                System.Math.Max(maxRefs, 50), null, null, cancellationToken).ConfigureAwait(false);
            result.TotalCalls = refs.Total;
            result.Calls = refs.Locations.Take(maxRefs).ToList();
        }
        catch { /* callers fetch is non-fatal */ }

        return result;
    }

    private static int EstimateColumn(CppFileDecl decl)
    {
        // CppOutlineParser doesn't track exact columns; approximate by finding the name on the line.
        if (string.IsNullOrEmpty(decl.Signature) || string.IsNullOrEmpty(decl.Name)) return 1;
        var idx = decl.Signature.IndexOf(decl.Name, StringComparison.Ordinal);
        return idx < 0 ? 1 : idx + 1;
    }

    // ---- cpp_replace_member ----

    public async Task<CppReplaceMemberResult> CppReplaceMemberAsync(string file, string className, string memberName, string newCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(memberName)) throw new VsmcpException(ErrorCodes.NotFound, "memberName is required.");
        if (newCode is null) throw new VsmcpException(ErrorCodes.NotFound, "newCode is required.");

        var outline = CppOutlineParser.Parse(file);
        var hit = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "method" || d.Kind == "function")
            && d.Name == memberName
            && d.Container is not null
            && (d.Container == className || d.Container.EndsWith("::" + className, StringComparison.Ordinal)));
        if (hit is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Member '{className}::{memberName}' not found in '{file}'.");

        var lines = File.ReadAllLines(file);
        int startLineIdx = hit.Line - 1;
        int endLineIdx = FindMemberEndLine(lines, startLineIdx);

        // Replace via FileReplaceRange.
        await FileReplaceRangeAsync(file, new FileRange
        {
            StartLine = startLineIdx + 1,
            StartColumn = 1,
            EndLine = endLineIdx + 1,
            EndColumn = lines[endLineIdx].Length + 1,
        }, newCode.TrimEnd('\r', '\n'), cancellationToken).ConfigureAwait(false);

        // Invalidate libclang's cached TU so subsequent semantic queries re-parse.
        try { await CppInvalidateAsync(file, cancellationToken).ConfigureAwait(false); } catch { /* analyzer not running yet is fine */ }

        return new CppReplaceMemberResult
        {
            File = file,
            ClassName = className,
            MemberName = memberName,
            Replaced = true,
            StartLine = startLineIdx + 1,
            EndLine = endLineIdx + 1,
        };
    }

    // ---- cpp_generate_constructor ----

    public async Task<CppGenerateCtorResult> CppGenerateConstructorAsync(string file, string className, IReadOnlyList<string>? memberNames, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        await Task.Yield();

        // Find the class declaration via outline.
        var outline = CppOutlineParser.Parse(file);
        var classDecl = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct") && d.Name == className);
        if (classDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Class/struct '{className}' not found in '{file}'.");

        // Walk lines from class start to find member field declarations + the closing brace.
        var lines = File.ReadAllLines(file);
        int classStartLineIdx = classDecl.Line - 1;
        int classEndLineIdx = FindMemberEndLine(lines, classStartLineIdx);

        // Discover fields: simple regex over the class body (non-method lines that look like `Type name;`).
        var fieldRx = new Regex(@"^\s*(?<type>[\w:<>,\s\*\&]+?)\s+(?<name>[A-Za-z_]\w*)\s*(?:=\s*[^;]+)?;\s*(?:\/\/.*)?$",
            RegexOptions.Compiled);
        var fields = new List<(string Type, string Name)>();
        for (int i = classStartLineIdx + 1; i < classEndLineIdx; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//") || trimmed.StartsWith("/*")) continue;
            if (trimmed.StartsWith("public:") || trimmed.StartsWith("private:") || trimmed.StartsWith("protected:")) continue;
            if (trimmed.Contains("(")) continue; // skip method declarations
            var m = fieldRx.Match(line);
            if (!m.Success) continue;
            var name = m.Groups["name"].Value;
            var type = m.Groups["type"].Value.Trim();
            // Drop access/storage modifiers from the type prefix, but KEEP const/volatile —
            // they're part of the type signature and matter for the generated parameter (e.g.
            // 'const char*' must stay const in the constructor parameter).
            type = Regex.Replace(type, @"\b(?:static|mutable|inline|constexpr)\b", "").Trim();
            if (string.IsNullOrEmpty(type)) continue;
            if (memberNames is not null && memberNames.Count > 0 && !memberNames.Contains(name)) continue;
            fields.Add((type, name));
        }

        if (fields.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, $"No member fields found in '{className}' to generate a constructor for.");

        // Build the constructor signature + member-initializer list.
        var ctor = new StringBuilder();
        ctor.Append("    ").Append(className).Append('(');
        ctor.Append(string.Join(", ", fields.Select(f => $"{f.Type} {f.Name}_")));
        ctor.Append(')');
        ctor.AppendLine();
        ctor.Append("        : ");
        ctor.Append(string.Join(", ", fields.Select(f => $"{f.Name}({f.Name}_)")));
        ctor.AppendLine();
        ctor.AppendLine("    {");
        ctor.AppendLine("    }");

        // Insert just before the class's closing brace.
        var insertLineIdx = classEndLineIdx; // line of '}'
        var ctorText = ctor.ToString();

        await FileReplaceRangeAsync(file, new FileRange
        {
            StartLine = insertLineIdx + 1,
            StartColumn = 1,
            EndLine = insertLineIdx + 1,
            EndColumn = 1,
        }, ctorText, cancellationToken).ConfigureAwait(false);

        try { await CppInvalidateAsync(file, cancellationToken).ConfigureAwait(false); } catch { }

        return new CppGenerateCtorResult
        {
            File = file,
            ClassName = className,
            Inserted = true,
            InsertedAtLine = insertLineIdx + 1,
            Code = ctorText.TrimEnd('\r', '\n'),
        };
    }

    // ---- cpp_override_member ----

    public async Task<CppOverrideMemberResult> CppOverrideMemberAsync(string file, string className, string methodName, string returnType, string parameters, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(methodName)) throw new VsmcpException(ErrorCodes.NotFound, "methodName is required.");
        if (string.IsNullOrEmpty(returnType)) throw new VsmcpException(ErrorCodes.NotFound, "returnType is required.");
        if (parameters is null) parameters = "";
        await Task.Yield();

        var outline = CppOutlineParser.Parse(file);
        var classDecl = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct") && d.Name == className);
        if (classDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Class/struct '{className}' not found in '{file}'.");

        var lines = File.ReadAllLines(file);
        int classStartLineIdx = classDecl.Line - 1;
        int classEndLineIdx = FindMemberEndLine(lines, classStartLineIdx);

        var sb = new StringBuilder();
        sb.Append("    ").Append(returnType).Append(' ').Append(methodName).Append('(').Append(parameters).Append(") override").AppendLine();
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: implement");
        sb.AppendLine("    }");
        var code = sb.ToString();

        await FileReplaceRangeAsync(file, new FileRange
        {
            StartLine = classEndLineIdx + 1,
            StartColumn = 1,
            EndLine = classEndLineIdx + 1,
            EndColumn = 1,
        }, code, cancellationToken).ConfigureAwait(false);

        try { await CppInvalidateAsync(file, cancellationToken).ConfigureAwait(false); } catch { }

        return new CppOverrideMemberResult
        {
            File = file,
            ClassName = className,
            MethodName = methodName,
            Inserted = true,
            InsertedAtLine = classEndLineIdx + 1,
            Code = code.TrimEnd('\r', '\n'),
        };
    }
}
