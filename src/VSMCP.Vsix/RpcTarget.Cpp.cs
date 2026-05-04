using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- C++ Extensions (M17) --------
    //
    // C++ has no Roslyn equivalent in the public VS SDK. These tools use:
    //   - text scanning (#include, #define) for macros and dependencies
    //   - solution-wide file enumeration (FileListAsync) for symbol search
    //   - DTE for navigation
    // No semantic analysis. For full IntelliSense fidelity, use VS in-editor.

    private static readonly Regex s_cppDefineRx = new(
        @"^\s*#\s*define\s+(?<name>\w+)(?:\s*\((?<args>[^)]*)\))?\s*(?<body>.*?)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public async Task<HeaderLookupResult> CppHeaderLookupAsync(
        string file, string symbolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(symbolName)) throw new VsmcpException(ErrorCodes.NotFound, "symbolName is required.");

        // Look at #include chain from the file, search each header for a declaration of `symbolName`.
        var deps = await FileDependenciesAsync(file, cancellationToken).ConfigureAwait(false);
        var dir = Path.GetDirectoryName(file)!;
        // Patterns: function decl `<retval> name(...)`, type alias `class name`, `struct name`, `typedef ... name`.
        var rx = new Regex(
            $@"\b(?:class|struct|enum)\s+{Regex.Escape(symbolName)}\b" +
            $@"|\b{Regex.Escape(symbolName)}\s*\(" +
            $@"|\btypedef\s+\w[\w\s\*\&]*\s+{Regex.Escape(symbolName)}\b" +
            $@"|\busing\s+{Regex.Escape(symbolName)}\s*=",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var inc in deps.Includes.Where(d => d.Type == "local"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hdrPath = Path.IsPathRooted(inc.File) ? inc.File : Path.Combine(dir, inc.File);
            if (!File.Exists(hdrPath)) continue;
            string[] lines;
            try { lines = File.ReadAllLines(hdrPath); }
            catch { continue; }
            for (int i = 0; i < lines.Length; i++)
            {
                if (rx.IsMatch(lines[i]))
                {
                    return new HeaderLookupResult
                    {
                        Header = new CodeSpan
                        {
                            File = hdrPath,
                            StartLine = i + 1,
                            StartColumn = 1,
                            EndLine = i + 1,
                            EndColumn = lines[i].Length + 1,
                        },
                        Type = lines[i].Trim(),
                    };
                }
            }
        }
        throw new VsmcpException(ErrorCodes.NotFound, $"Symbol '{symbolName}' not found in any include of '{file}'.");
    }

    public async Task<IncludeChainResult> CppIncludeChainAsync(
        string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await Task.Yield();
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var result = new IncludeChainResult();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await WalkIncludesAsync(file, visited, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task WalkIncludesAsync(string file, HashSet<string> visited, IncludeChainResult result, CancellationToken ct)
    {
        if (!visited.Add(Path.GetFullPath(file))) return;

        var deps = await FileDependenciesAsync(file, ct).ConfigureAwait(false);
        var dir = Path.GetDirectoryName(file)!;
        foreach (var inc in deps.Includes)
        {
            ct.ThrowIfCancellationRequested();
            var hdrPath = inc.Type == "local" && !Path.IsPathRooted(inc.File)
                ? Path.Combine(dir, inc.File)
                : inc.File;

            result.Chain.Add(new IncludeChainItem
            {
                File = hdrPath,
                Line = inc.Line,
                Type = inc.Type,
            });

            if (inc.Type == "local" && File.Exists(hdrPath))
                await WalkIncludesAsync(hdrPath, visited, result, ct).ConfigureAwait(false);
        }
    }

    public async Task<MacroResult> CppMacroLookupAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name)) throw new VsmcpException(ErrorCodes.NotFound, "name is required.");

        var files = await FileListAsync(null, null, "*.{h,hpp,hxx,c,cpp,cc,cxx}",
            new[] { "file" }, 50_000, cancellationToken).ConfigureAwait(false);

        var rx = new Regex(
            $@"^\s*#\s*define\s+{Regex.Escape(name)}\b(?:\s*\([^)]*\))?\s*(?<body>.*)$",
            RegexOptions.Compiled);
        var users = new List<CodeSpan>();
        MacroDefinition? def = null;

        foreach (var f in files.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] lines;
            try { lines = File.ReadAllLines(f.Path); }
            catch { continue; }
            for (int i = 0; i < lines.Length; i++)
            {
                var m = rx.Match(lines[i]);
                if (m.Success && def is null)
                {
                    def = new MacroDefinition
                    {
                        Location = new CodeSpan
                        {
                            File = f.Path,
                            StartLine = i + 1, StartColumn = 1,
                            EndLine = i + 1, EndColumn = lines[i].Length + 1,
                        },
                        Expansion = m.Groups["body"].Value.Trim(),
                    };
                }
                else if (Regex.IsMatch(lines[i], $@"\b{Regex.Escape(name)}\b"))
                {
                    users.Add(new CodeSpan
                    {
                        File = f.Path,
                        StartLine = i + 1, StartColumn = 1,
                        EndLine = i + 1, EndColumn = lines[i].Length + 1,
                    });
                }
            }
        }

        return new MacroResult
        {
            Definition = def ?? new MacroDefinition(),
            Users = users,
        };
    }

    public async Task<PreprocessResult> CppPreprocessAsync(
        string file, IReadOnlyList<string>? defines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        var clExe = await ResolveClExeAsync(cancellationToken).ConfigureAwait(false);

        var defineArgs = defines is null
            ? ""
            : string.Join(" ", defines.Where(d => !string.IsNullOrEmpty(d)).Select(d => $"/D{d}"));

        var outFile = Path.ChangeExtension(file, ".i");
        var args = $"/nologo /P /Fi\"{outFile}\" {defineArgs} \"{file}\"";

        await RunProcessAsync(clExe, args, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(outFile))
            throw new VsmcpException(ErrorCodes.InteropFault, $"cl.exe did not produce {outFile} — check stderr for errors.");

        var source = File.ReadAllText(outFile);
        var lineMap = new List<LineMapItem>();
        // cl.exe emits #line directives mapping back to source lines. Parse them.
        var rx = new System.Text.RegularExpressions.Regex(@"^\s*#line\s+(\d+)\s+", System.Text.RegularExpressions.RegexOptions.Multiline);
        var preprocLine = 1;
        foreach (var line in source.Split('\n'))
        {
            var m = rx.Match(line);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var srcLine))
                lineMap.Add(new LineMapItem { SourceLine = srcLine, PreprocLine = preprocLine });
            preprocLine++;
        }
        return new PreprocessResult { Source = source, LineMap = lineMap };
    }

    private static async Task<string> ResolveClExeAsync(CancellationToken ct)
    {
        // Try vswhere.exe (ships with VS Installer) to find the latest VS install with VC tools.
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            throw new VsmcpException(ErrorCodes.Unsupported, $"vswhere.exe not found at {vswhere}. Install VS or run from a Developer Command Prompt.");

        var output = await RunProcessAsync(vswhere,
            "-latest -products * -requires Microsoft.VisualCpp.Tools.Host.x86 -property installationPath",
            ct).ConfigureAwait(false);
        var installPath = output.Trim().Split('\n').FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            throw new VsmcpException(ErrorCodes.Unsupported, "No VS install with VC++ tools found.");

        var versionTxt = Path.Combine(installPath!, "VC", "Auxiliary", "Build", "Microsoft.VCToolsVersion.default.txt");
        if (!File.Exists(versionTxt))
            throw new VsmcpException(ErrorCodes.Unsupported, $"VC tools version file not found: {versionTxt}");

        var version = File.ReadAllText(versionTxt).Trim();
        var clx64 = Path.Combine(installPath, "VC", "Tools", "MSVC", version, "bin", "Hostx64", "x64", "cl.exe");
        if (File.Exists(clx64)) return clx64;
        var clx86 = Path.Combine(installPath, "VC", "Tools", "MSVC", version, "bin", "Hostx86", "x86", "cl.exe");
        if (File.Exists(clx86)) return clx86;
        throw new VsmcpException(ErrorCodes.Unsupported, $"cl.exe not found under VC tools {version}.");
    }

    public async Task<ApiReferenceResult> CppApiRefAsync(
        string apiName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(apiName)) throw new VsmcpException(ErrorCodes.NotFound, "apiName is required.");

        // Search across all C/C++ source + header files in the solution for a declaration.
        var files = await FileListAsync(null, null, "*.{h,hpp,hxx,c,cpp,cc,cxx}",
            new[] { "file" }, 50_000, cancellationToken).ConfigureAwait(false);

        var declRx = new System.Text.RegularExpressions.Regex(
            $@"\b(?:class|struct|enum|typedef|using)\s+{System.Text.RegularExpressions.Regex.Escape(apiName)}\b" +
            $@"|^\s*[\w\*\&:<>,\s]+\s+{System.Text.RegularExpressions.Regex.Escape(apiName)}\s*\(",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

        foreach (var f in files.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] lines;
            try { lines = File.ReadAllLines(f.Path); }
            catch { continue; }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!declRx.IsMatch(lines[i])) continue;

                // Walk backwards to gather any /** ... */ or /// ... documentation block.
                var docs = new List<string>();
                for (int j = i - 1; j >= 0; j--)
                {
                    var t = lines[j].TrimStart();
                    if (t.StartsWith("///", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal))
                        docs.Insert(0, t);
                    else if (t.StartsWith("*", StringComparison.Ordinal) || t.EndsWith("*/", StringComparison.Ordinal) || t.StartsWith("/**", StringComparison.Ordinal))
                        docs.Insert(0, t);
                    else break;
                }

                return new ApiReferenceResult
                {
                    Name = apiName,
                    Type = lines[i].Contains("class ") ? "class" :
                           lines[i].Contains("struct ") ? "struct" :
                           lines[i].Contains("enum ") ? "enum" :
                           lines[i].Contains("typedef ") ? "typedef" :
                           "function",
                    Declaration = lines[i].Trim(),
                    Documentation = docs.Count > 0 ? string.Join("\n", docs) : null,
                    HeaderFile = f.Path,
                };
            }
        }

        throw new VsmcpException(ErrorCodes.NotFound, $"No declaration of '{apiName}' found in solution C/C++ files.");
    }

    public async Task<GeneratedFileInfo> CppGeneratedFileAsync(
        string file, string type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var fileFullPath = Path.GetFullPath(file);

        // Walk every C++ project's .vcxproj and look for <CustomBuild> items whose <Outputs>
        // contains the queried file (or whose source generator type matches).
        foreach (var project in VsHelpers.EnumerateProjects(dte.Solution))
        {
            string? projPath = null;
            try { projPath = project.FullName; } catch { }
            if (string.IsNullOrEmpty(projPath) || !File.Exists(projPath)) continue;
            if (!projPath!.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)) continue;

            System.Xml.Linq.XDocument xml;
            try { xml = System.Xml.Linq.XDocument.Load(projPath); }
            catch { continue; }

            foreach (var cb in xml.Descendants().Where(e => e.Name.LocalName == "CustomBuild"))
            {
                var outputs = cb.Element(System.Xml.Linq.XName.Get("Outputs", cb.Name.NamespaceName))?.Value ?? "";
                var include = cb.Attribute("Include")?.Value ?? "";

                var outputsAbs = outputs.Split(';')
                    .Select(o => Path.IsPathRooted(o)
                        ? o
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projPath)!, o.Trim())))
                    .ToList();

                if (outputsAbs.Any(o => string.Equals(o, fileFullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    var generatorAbs = Path.IsPathRooted(include)
                        ? include
                        : Path.Combine(Path.GetDirectoryName(projPath)!, include);
                    return new GeneratedFileInfo
                    {
                        GeneratedFile = fileFullPath,
                        GeneratedFrom = generatorAbs,
                        LineMap = new List<LineMapItem>(),
                    };
                }
            }
        }

        throw new VsmcpException(ErrorCodes.NotFound,
            $"No <CustomBuild> rule in any .vcxproj produces '{fileFullPath}'. " +
            "Either the file isn't generated, or its build rule lives outside the project files (e.g. CMake).");
    }
}
