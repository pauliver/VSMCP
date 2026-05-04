using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // Per-target session scope (#82). Lives for the connection's lifetime.
    private SessionScope? _session;
    private long _lastBuildAtMs;
    private string? _lastBuildOutcome;

    // -------- Phase 1 --------

    public async Task<BuildSummaryResult> BuildSummaryAsync(string buildId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(buildId)) throw new VsmcpException(ErrorCodes.NotFound, "buildId is required.");

        var status = await BuildStatusAsync(buildId, cancellationToken).ConfigureAwait(false);
        var errors = await BuildErrorsAsync(buildId, cancellationToken).ConfigureAwait(false);
        var warnings = await BuildWarningsAsync(buildId, cancellationToken).ConfigureAwait(false);

        var byProject = new Dictionary<string, BuildProjectSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in errors)
        {
            var key = e.Project ?? "<solution>";
            if (!byProject.TryGetValue(key, out var p))
            {
                p = new BuildProjectSummary { Name = key, Status = "Failed" };
                byProject[key] = p;
            }
            p.Errors++;
            if (p.FirstError is null)
            {
                p.FirstError = CtxHelpers.Compact(e, CodeDiagnosticSeverity.Error);
                p.FirstErrorFile = e.File;
            }
        }
        foreach (var w in warnings)
        {
            var key = w.Project ?? "<solution>";
            if (!byProject.TryGetValue(key, out var p))
            {
                p = new BuildProjectSummary { Name = key, Status = "Succeeded" };
                byProject[key] = p;
            }
            p.Warnings++;
        }

        // Add succeeded projects that had no diagnostics.
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte)
        {
            foreach (var proj in VsHelpers.EnumerateProjects(dte.Solution))
            {
                var name = proj.Name ?? proj.UniqueName ?? "";
                if (!byProject.ContainsKey(name))
                    byProject[name] = new BuildProjectSummary { Name = name, Status = "Succeeded" };
            }
        }

        var outcome = status.State.ToString();
        _lastBuildAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _lastBuildOutcome = outcome;

        long duration = 0;
        if (status.EndedAtMs is long ended && ended > 0 && status.StartedAtMs > 0)
            duration = ended - status.StartedAtMs;

        return new BuildSummaryResult
        {
            BuildId = buildId,
            Outcome = outcome,
            Projects = byProject.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList(),
            TotalErrors = status.ErrorCount,
            TotalWarnings = status.WarningCount,
            DurationMs = duration,
        };
    }

    public async Task<TestSummaryResult> TestRunSummaryAsync(
        string? filter, string? projectId, string? configuration, string mode,
        CancellationToken cancellationToken = default)
    {
        var full = await TestRunAsync(filter, projectId, configuration, cancellationToken).ConfigureAwait(false);
        mode = (mode ?? "summary").ToLowerInvariant();

        var summary = new TestSummaryResult
        {
            RunId = full.RunId,
            Passed = full.Passed,
            Failed = full.Failed,
            Skipped = full.Skipped,
            DurationMs = full.Results.Sum(r => r.DurationMs),
        };

        if (mode == "full")
        {
            summary.Failures = full.Results.Where(r => r.Outcome == TestOutcome.Failed).ToList();
            summary.OutputTail = full.Output;
            return summary;
        }

        var failures = full.Results.Where(r => r.Outcome == TestOutcome.Failed).ToList();
        summary.Failures = mode == "summary" ? failures.Take(5).ToList() : failures;

        if (mode == "summary" && failures.Count > 5)
        {
            summary.OutputTail = $"… and {failures.Count - 5} more failures. Call with mode='failures' to see all.";
        }
        else if (mode == "failures")
        {
            // Tail of vstest output — last 20 lines.
            if (!string.IsNullOrEmpty(full.Output))
            {
                var lines = full.Output!.Split('\n');
                var tail = lines.Skip(Math.Max(0, lines.Length - 20));
                summary.OutputTail = string.Join("\n", tail);
            }
        }
        return summary;
    }

    public async Task<GroupedDiagnosticsResult> CodeDiagnosticsGroupedAsync(
        string? file, int maxResults, CancellationToken cancellationToken = default)
    {
        var raw = await CodeDiagnosticsAsync(file, maxResults <= 0 ? 1000 : maxResults, cancellationToken).ConfigureAwait(false);
        var grouped = CtxHelpers.GroupDiagnostics(raw.Diagnostics);
        grouped.Truncated = raw.Truncated;
        return grouped;
    }

    public async Task<GroupedDiagnosticsResult> BuildErrorsGroupedAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var errors = await BuildErrorsAsync(buildId, cancellationToken).ConfigureAwait(false);
        var warnings = await BuildWarningsAsync(buildId, cancellationToken).ConfigureAwait(false);
        return CtxHelpers.GroupBuildDiagnostics(errors, warnings);
    }

    // -------- Phase 2 --------

    public async Task<FileReadIfChangedResult> FileReadIfChangedAsync(
        string path, string? knownHash, FileRange? range, CancellationToken cancellationToken = default)
    {
        var read = await FileReadAsync(path, range, cancellationToken).ConfigureAwait(false);
        var hash = CtxHelpers.Sha1Hex(read.Content);
        if (!string.IsNullOrEmpty(knownHash) && string.Equals(hash, knownHash, StringComparison.Ordinal))
        {
            return new FileReadIfChangedResult
            {
                Path = path,
                Unchanged = true,
                ContentHash = hash,
                Result = null,
            };
        }
        return new FileReadIfChangedResult
        {
            Path = path,
            Unchanged = false,
            ContentHash = hash,
            Result = read,
        };
    }

    public async Task<CodeDiffResult> CodeDiffAsync(
        string file, string? baseHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var current = await FileReadAsync(file, null, cancellationToken).ConfigureAwait(false);
        var toHash = CtxHelpers.Sha1Hex(current.Content);

        // Try git first (most useful when called without baseHash).
        if (string.IsNullOrEmpty(baseHash))
        {
            var (ok, fromText, fromHash) = TryReadGitHead(file);
            if (ok)
            {
                return new CodeDiffResult
                {
                    File = file,
                    FromHash = fromHash,
                    ToHash = toHash,
                    Hunks = CtxHelpers.TextDiff(fromText, current.Content),
                };
            }
            // Fall back to "no baseline" — return a hunk that's "everything is new".
            return new CodeDiffResult
            {
                File = file,
                FromHash = "",
                ToHash = toHash,
                Hunks = new List<DiffHunk>
                {
                    new() { StartLine = 1, AddedLines = current.Content.Replace("\r\n", "\n").Split('\n').ToList() }
                },
            };
        }

        // baseHash provided — caller is asserting "I had this content".
        // We can't reconstruct the prior text from a hash; we can only confirm it differs.
        return new CodeDiffResult
        {
            File = file,
            FromHash = baseHash,
            ToHash = toHash,
            Hunks = string.Equals(baseHash, toHash, StringComparison.Ordinal)
                ? new List<DiffHunk>()
                : new List<DiffHunk>
                {
                    new() { StartLine = 1,
                            AddedLines = new List<string> { "<baseHash known but content not cached server-side; pass baseHash=null to diff against git HEAD>" } }
                },
        };
    }

    private static (bool ok, string text, string hash) TryReadGitHead(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(file));
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                {
                    var rel = Path.GetFullPath(file).Substring(dir.Length + 1).Replace('\\', '/');
                    var output = RunGit(dir!, $"show HEAD:\"{rel}\"");
                    if (output is not null) return (true, output, "HEAD");
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return (false, "", "");
    }

    private static string? RunGit(string workdir, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    // -------- Phase 3 --------

    public async Task<FileOutlineResult> FileOutlineAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        var read = await FileReadAsync(file, null, cancellationToken).ConfigureAwait(false);
        var hash = CtxHelpers.Sha1Hex(read.Content);
        var result = new FileOutlineResult { File = file, ContentHash = hash };

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file);
        if (doc is null)
        {
            // Non-Roslyn file — just return the lines untouched (best we can do).
            result.Lines = read.Content.Replace("\r\n", "\n").Split('\n').ToList();
            return result;
        }

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null)
        {
            result.Lines = read.Content.Replace("\r\n", "\n").Split('\n').ToList();
            return result;
        }

        // Usings + namespace header verbatim; types abbreviated; members signature-only with line markers.
        foreach (var u in root.Usings) result.Lines.Add(u.ToString().TrimEnd());
        if (root.Usings.Count > 0) result.Lines.Add("");

        foreach (var member in root.Members) AppendOutlineNode(member, result.Lines, indent: 0);
        return result;
    }

    private static void AppendOutlineNode(MemberDeclarationSyntax node, List<string> into, int indent)
    {
        var pad = new string(' ', indent * 4);
        if (node is BaseNamespaceDeclarationSyntax ns)
        {
            into.Add($"{pad}namespace {ns.Name}");
            into.Add($"{pad}{{");
            foreach (var m in ns.Members) AppendOutlineNode(m, into, indent + 1);
            into.Add($"{pad}}}");
            return;
        }
        if (node is FileScopedNamespaceDeclarationSyntax fns)
        {
            into.Add($"{pad}namespace {fns.Name};");
            into.Add("");
            foreach (var m in fns.Members) AppendOutlineNode(m, into, indent);
            return;
        }
        if (node is BaseTypeDeclarationSyntax type)
        {
            var line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var hdr = ExtractTypeHeader(type);
            into.Add($"{pad}{hdr}    // L{line}");
            into.Add($"{pad}{{");
            if (type is TypeDeclarationSyntax td)
                foreach (var member in td.Members) AppendOutlineNode(member, into, indent + 1);
            into.Add($"{pad}}}");
            return;
        }
        // Methods, properties, fields, etc.
        var memberLine = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var sig = ExtractMemberSignature(node);
        into.Add($"{pad}{sig}    // L{memberLine}");
    }

    private static string ExtractTypeHeader(BaseTypeDeclarationSyntax type)
    {
        var modifiers = string.Join(" ", type.Modifiers.Select(m => m.Text));
        var keyword = type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "" ? "record" : $"record {r.ClassOrStructKeyword.Text}",
            EnumDeclarationSyntax => "enum",
            _ => "type",
        };
        var bases = type.BaseList?.ToString() ?? "";
        var ident = type.Identifier.Text;
        var generics = (type as TypeDeclarationSyntax)?.TypeParameterList?.ToString() ?? "";
        return $"{modifiers} {keyword} {ident}{generics} {bases}".Trim();
    }

    private static string ExtractMemberSignature(MemberDeclarationSyntax node) => node switch
    {
        MethodDeclarationSyntax m => $"{string.Join(" ", m.Modifiers.Select(x => x.Text))} {m.ReturnType} {m.Identifier}{m.TypeParameterList}{m.ParameterList} {{ ... }}".Trim(),
        ConstructorDeclarationSyntax c => $"{string.Join(" ", c.Modifiers.Select(x => x.Text))} {c.Identifier}{c.ParameterList} {{ ... }}".Trim(),
        DestructorDeclarationSyntax d => $"~{d.Identifier}() {{ ... }}",
        PropertyDeclarationSyntax p => $"{string.Join(" ", p.Modifiers.Select(x => x.Text))} {p.Type} {p.Identifier} {{ ... }}".Trim(),
        FieldDeclarationSyntax f => $"{string.Join(" ", f.Modifiers.Select(x => x.Text))} {f.Declaration};".Trim(),
        EventDeclarationSyntax e => $"{string.Join(" ", e.Modifiers.Select(x => x.Text))} event {e.Type} {e.Identifier};".Trim(),
        EventFieldDeclarationSyntax ef => $"{string.Join(" ", ef.Modifiers.Select(x => x.Text))} event {ef.Declaration};".Trim(),
        DelegateDeclarationSyntax dl => $"{string.Join(" ", dl.Modifiers.Select(x => x.Text))} delegate {dl.ReturnType} {dl.Identifier}{dl.ParameterList};".Trim(),
        OperatorDeclarationSyntax op => $"{string.Join(" ", op.Modifiers.Select(x => x.Text))} {op.ReturnType} operator {op.OperatorToken}{op.ParameterList} {{ ... }}".Trim(),
        ConversionOperatorDeclarationSyntax co => $"{string.Join(" ", co.Modifiers.Select(x => x.Text))} {co.ImplicitOrExplicitKeyword} operator {co.Type}{co.ParameterList} {{ ... }}".Trim(),
        IndexerDeclarationSyntax ix => $"{string.Join(" ", ix.Modifiers.Select(x => x.Text))} {ix.Type} this{ix.ParameterList} {{ ... }}".Trim(),
        EnumMemberDeclarationSyntax em => $"{em.Identifier},",
        _ => node.ToString().Split('\n')[0].Trim() + " ...",
    };

    public async Task<FileInfoResult> FileInfoAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {fullPath}");

        var bytes = File.ReadAllBytes(fullPath);
        var encoding = CtxHelpers.DetectEncoding(fullPath);
        var content = File.ReadAllText(fullPath);
        var lineCount = content.Count(c => c == '\n') + 1;

        var result = new FileInfoResult
        {
            Path = fullPath,
            Language = GetLanguage(fullPath),
            Encoding = encoding,
            LineCount = lineCount,
            ByteSize = bytes.Length,
            ContentHash = CtxHelpers.Sha1Hex(bytes),
        };

        // Project + namespace lookup (Roslyn).
        try
        {
            var ws = await GetWorkspaceAsync(cancellationToken);
            var doc = FindDocument(ws.CurrentSolution, fullPath);
            if (doc is not null)
            {
                result.Project = doc.Project.Name;
                result.Namespace = doc.Project.DefaultNamespace;
                var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is not null)
                {
                    var ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                    if (ns is not null) result.Namespace = ns.Name.ToString();
                    result.OutlineDepth = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Count();
                }
            }
        }
        catch { /* non-Roslyn file — leave Project/Namespace null */ }

        // Heuristics.
        var firstFewLines = content.Length < 500 ? content : content.Substring(0, 500);
        result.IsGenerated = Regex.IsMatch(firstFewLines, @"//\s*<auto-generated", RegexOptions.IgnoreCase)
            || fullPath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fullPath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        result.IsTest = (result.Project?.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0)
            || (result.Namespace?.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0)
            || fullPath.IndexOf(".tests.", StringComparison.OrdinalIgnoreCase) >= 0
            || fullPath.IndexOf("\\tests\\", StringComparison.OrdinalIgnoreCase) >= 0;

        // Open in editor?
        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, fullPath);
        if (buffer is not null)
        {
            result.OpenInEditor = true;
            try
            {
                if (buffer.Properties.TryGetProperty<Microsoft.VisualStudio.Text.ITextDocument>(typeof(Microsoft.VisualStudio.Text.ITextDocument), out var d))
                    result.HasUnsavedChanges = d.IsDirty;
            }
            catch { }
        }
        return result;
    }

    public async Task<SymbolSummaryResult> CodeSymbolSummaryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");
        var match = await CodeFindSymbolAsync(symbol, kind: null, maxResults: 1, cancellationToken).ConfigureAwait(false);
        var first = match.Matches.FirstOrDefault();
        if (first is null) throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbol}");

        var result = new SymbolSummaryResult { Symbol = first };
        if (first.Location is null) return result;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, first.Location.File);
        if (doc is null) return result;

        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) return result;

        var pos = first.Location;
        var node = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(n =>
            {
                var s = n.GetLocation().GetLineSpan().StartLinePosition;
                return s.Line + 1 == pos.StartLine;
            });
        if (node is null) return result;

        var spanLines = node.GetLocation().GetLineSpan();
        result.LineCount = spanLines.EndLinePosition.Line - spanLines.StartLinePosition.Line + 1;

        if (node is MethodDeclarationSyntax method)
        {
            var declSym = sm.GetDeclaredSymbol(method) as IMethodSymbol;
            result.IsAsync = declSym?.IsAsync ?? false;
            result.Returns = declSym?.ReturnType.ToDisplayString();

            // Calls: invocations
            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var s = sm.GetSymbolInfo(inv).Symbol;
                if (s is null) continue;
                calls.Add(s.ToDisplayString(CtxHelpers.ToFormat(SymbolDisplayMode.Qualified)));
                if (calls.Count >= 30) break;
            }
            result.Calls = calls.ToList();

            // Touches: field references
            var touches = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ident in method.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var s = sm.GetSymbolInfo(ident).Symbol;
                if (s is IFieldSymbol f && SymbolEqualityComparer.Default.Equals(f.ContainingType, declSym?.ContainingType))
                    touches.Add(f.Name);
            }
            result.Touches = touches.ToList();

            // Throws
            foreach (var t in method.DescendantNodes().OfType<ThrowStatementSyntax>())
            {
                var s = sm.GetTypeInfo(t.Expression!).Type;
                if (s is not null) result.Throws.Add(s.Name);
            }
            foreach (var t in method.DescendantNodes().OfType<ThrowExpressionSyntax>())
            {
                var s = sm.GetTypeInfo(t.Expression).Type;
                if (s is not null) result.Throws.Add(s.Name);
            }
            result.Throws = result.Throws.Distinct().ToList();

            // Cyclomatic + awaits
            int cyc = 1;
            foreach (var _ in method.DescendantNodes().OfType<IfStatementSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<SwitchSectionSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<WhileStatementSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<ForStatementSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<ForEachStatementSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<CatchClauseSyntax>()) cyc++;
            foreach (var _ in method.DescendantNodes().OfType<ConditionalExpressionSyntax>()) cyc++;
            cyc += method.DescendantTokens()
                .Count(t => t.IsKind(SyntaxKind.AmpersandAmpersandToken) || t.IsKind(SyntaxKind.BarBarToken));
            result.Cyclomatic = cyc;
            result.Awaits = method.DescendantNodes().OfType<AwaitExpressionSyntax>().Count();
        }
        return result;
    }

    public async Task<InvestigateResult> CodeInvestigateAsync(
        string symbol, int maxRefs, bool includeTests, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new VsmcpException(ErrorCodes.NotFound, "symbol is required.");
        if (maxRefs <= 0) maxRefs = 50;

        var matches = await CodeFindSymbolAsync(symbol, kind: null, maxResults: 1, cancellationToken).ConfigureAwait(false);
        var first = matches.Matches.FirstOrDefault();
        if (first is null) throw new VsmcpException(ErrorCodes.NotFound, $"Symbol not found: {symbol}");

        var result = new InvestigateResult { Symbol = first };
        if (first.Location is null) return result;

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, first.Location.File);
        if (doc is null) return result;

        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) return result;

        // Body
        var declNode = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(n => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == first.Location.StartLine);
        if (declNode is not null)
            result.Body = declNode.ToFullString();

        // Symbol stats
        var sym = declNode is null ? null : sm.GetDeclaredSymbol(declNode);
        if (sym is not null)
        {
            result.Stats.IsStatic = sym.IsStatic;
            result.Stats.IsAbstract = sym.IsAbstract;
            result.Stats.IsVirtual = sym.IsVirtual;
            if (sym is IMethodSymbol ms) result.Stats.IsAsync = ms.IsAsync;
        }

        // Calls (in)
        if (sym is not null)
        {
            try
            {
                var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
                int count = 0;
                foreach (var refResult in refs)
                {
                    foreach (var loc in refResult.Locations)
                    {
                        if (count >= maxRefs) break;
                        var span = loc.Location.GetLineSpan();
                        result.Calls.Add(new InvestigateCallEntry
                        {
                            Symbol = "",
                            Location = new CodeSpan
                            {
                                File = span.Path ?? "",
                                StartLine = span.StartLinePosition.Line + 1,
                                StartColumn = span.StartLinePosition.Character + 1,
                                EndLine = span.EndLinePosition.Line + 1,
                                EndColumn = span.EndLinePosition.Character + 1,
                            },
                        });
                        count++;
                    }
                    if (count >= maxRefs) break;
                }
                result.Stats.ReferenceCount = count;
            }
            catch { }
        }

        // Calls (out): invocations inside body
        if (declNode is not null)
        {
            foreach (var inv in declNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var s = sm.GetSymbolInfo(inv).Symbol;
                if (s is null) continue;
                var span = inv.GetLocation().GetLineSpan();
                result.CallsOut.Add(new InvestigateCallEntry
                {
                    Symbol = s.ToDisplayString(CtxHelpers.ToFormat(SymbolDisplayMode.Qualified)),
                    Location = new CodeSpan
                    {
                        File = span.Path ?? "",
                        StartLine = span.StartLinePosition.Line + 1,
                        StartColumn = span.StartLinePosition.Character + 1,
                        EndLine = span.EndLinePosition.Line + 1,
                        EndColumn = span.EndLinePosition.Character + 1,
                    },
                });
            }
        }

        // Tests: heuristic — references whose containing type's name contains "Test"
        if (includeTests && sym is not null)
        {
            try
            {
                var refs = await SymbolFinder.FindReferencesAsync(sym, ws.CurrentSolution, cancellationToken).ConfigureAwait(false);
                foreach (var refResult in refs)
                {
                    foreach (var loc in refResult.Locations)
                    {
                        var docId = loc.Document.Id;
                        var refDoc = ws.CurrentSolution.GetDocument(docId);
                        if (refDoc is null) continue;
                        var refRoot = await refDoc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                        if (refRoot is null) continue;
                        var node = refRoot.FindNode(loc.Location.SourceSpan);
                        var containingType = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                        if (containingType is null) continue;
                        var name = containingType.Identifier.Text;
                        if (name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("Spec", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        var span = containingType.GetLocation().GetLineSpan();
                        result.Tests.Add(new InvestigateCallEntry
                        {
                            Symbol = name,
                            Location = new CodeSpan
                            {
                                File = span.Path ?? "",
                                StartLine = span.StartLinePosition.Line + 1,
                                StartColumn = span.StartLinePosition.Character + 1,
                                EndLine = span.EndLinePosition.Line + 1,
                                EndColumn = span.EndLinePosition.Character + 1,
                            },
                        });
                        if (result.Tests.Count >= 10) break;
                    }
                    if (result.Tests.Count >= 10) break;
                }
            }
            catch { }
        }
        return result;
    }

    // -------- Phase 4 --------

    public async Task<SessionScopeResult> SessionScopeAsync(
        IReadOnlyList<string>? symbols, string? project, string? folder,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _session = new SessionScope
        {
            Symbols = symbols?.ToList() ?? new List<string>(),
            Project = project,
            Folder = folder,
        };
        return new SessionScopeResult
        {
            ScopeId = _session.Id,
            ResolvedSymbols = _session.Symbols,
            ResolvedProject = _session.Project,
            ResolvedFolder = _session.Folder,
            EstablishedAtMs = _session.EstablishedAtMs,
        };
    }

    public async Task<SessionCurrentResult> SessionCurrentAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_session is null) return new SessionCurrentResult { Active = false };
        return new SessionCurrentResult
        {
            Symbols = _session.Symbols,
            Project = _session.Project,
            Folder = _session.Folder,
            EstablishedAtMs = _session.EstablishedAtMs,
            Active = true,
        };
    }

    public async Task SessionClearAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _session = null;
    }

    public async Task<IoContextResult> IoContextAsync(CancellationToken cancellationToken = default)
    {
        var editor = await EditorActiveAsync(cancellationToken).ConfigureAwait(false);
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        DebugInfo? debugger = null;
        try { debugger = await DebugStateAsync(cancellationToken).ConfigureAwait(false); } catch { }

        return new IoContextResult
        {
            Editor = editor,
            Solution = status,
            Debugger = debugger,
            LastBuildAtMs = _lastBuildAtMs == 0 ? null : _lastBuildAtMs,
            LastBuildOutcome = _lastBuildOutcome,
        };
    }
}
