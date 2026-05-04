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

    public Task<PreprocessResult> CppPreprocessAsync(
        string file, IReadOnlyList<string>? defines, CancellationToken cancellationToken = default)
    {
        // Full preprocessing requires invoking cl.exe with /P or /E. We don't ship a path discovery
        // here; users wanting preprocessed output should configure VS or invoke MSBuild directly.
        // Return Unsupported with a clear hint rather than a low-fidelity hand-rolled approximation.
        throw new VsmcpException(ErrorCodes.Unsupported,
            "cpp.preprocess requires invoking cl.exe and a full toolchain. " +
            "Run `cl /P <file>` from a Developer Command Prompt or use the VS Build menu's 'Preprocess File' action.");
    }

    public Task<ApiReferenceResult> CppApiRefAsync(
        string apiName, CancellationToken cancellationToken = default)
    {
        // No offline API DB ships with VSMCP. Defer to docs.microsoft.com lookup or IntelliSense.
        throw new VsmcpException(ErrorCodes.Unsupported,
            "cpp.api_ref requires an offline API database which is not bundled. " +
            "Use code.quick_info on a usage of the API for inline documentation, or open docs.microsoft.com.");
    }

    public Task<GeneratedFileInfo> CppGeneratedFileAsync(
        string file, string type, CancellationToken cancellationToken = default)
    {
        // Tracking generated files (e.g. MIDL, build-event-generated headers) requires per-project
        // build-system integration that isn't available through the generic VS automation surface.
        throw new VsmcpException(ErrorCodes.Unsupported,
            "cpp.generated_file requires build-system specific integration. " +
            "Inspect the project's pre-build steps in VS or its .vcxproj for generator outputs.");
    }
}
