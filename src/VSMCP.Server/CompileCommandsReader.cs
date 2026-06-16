using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Reads a CMake/Ninja <c>compile_commands.json</c> and resolves the include/define/std flags
/// for one source file (#135). JSON parsing lives here (Server, net9 System.Text.Json); the
/// flag-extraction logic is the pure VSMCP.Core.CompilerFlagExtractor. Relative include paths
/// are resolved to absolute against each entry's working directory so they can be passed
/// straight to the cpp_* semantic tools' extraIncludes/extraDefines.
/// </summary>
public static class CompileCommandsReader
{
    public static CppCompileFlagsResult Read(string compileCommandsPath, string file)
    {
        if (string.IsNullOrWhiteSpace(compileCommandsPath) || !File.Exists(compileCommandsPath))
            throw new VsmcpException(ErrorCodes.NotFound, $"compile_commands.json not found: {compileCommandsPath}");

        var json = File.ReadAllText(compileCommandsPath);
        var result = new CppCompileFlagsResult { File = file };

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new VsmcpException(ErrorCodes.WrongState, "compile_commands.json is not a JSON array.");

        var wantedFull = TryFullPath(file);

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var entryFile = entry.TryGetProperty("file", out var fe) ? fe.GetString() ?? "" : "";
            var dir = entry.TryGetProperty("directory", out var de) ? de.GetString() ?? "" : "";

            // Entry file may be relative to its directory.
            var entryFull = Path.IsPathRooted(entryFile) ? TryFullPath(entryFile)
                : !string.IsNullOrEmpty(dir) ? TryFullPath(Path.Combine(dir, entryFile))
                : entryFile;

            if (!PathsEqual(entryFull, wantedFull) && !PathsEqual(entryFile, file)) continue;

            CompilerFlags flags;
            if (entry.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Array)
                flags = CompilerFlagExtractor.Extract(args.EnumerateArray().Select(a => a.GetString() ?? "").ToList());
            else if (entry.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
                flags = CompilerFlagExtractor.ExtractFromCommand(cmd.GetString() ?? "");
            else
                flags = new CompilerFlags();

            result.Found = true;
            result.Directory = dir;
            result.Includes = flags.Includes.Select(p => ResolveAgainst(dir, p)).ToList();
            result.SystemIncludes = flags.SystemIncludes.Select(p => ResolveAgainst(dir, p)).ToList();
            result.Defines = flags.Defines;          // not paths
            result.Std = flags.Std;
            return result;
        }

        return result; // Found = false
    }

    private static string ResolveAgainst(string dir, string path)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrEmpty(dir)) return path;
        try { return Path.GetFullPath(Path.Combine(dir, path)); } catch { return path; }
    }

    private static string TryFullPath(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a?.Replace('\\', '/'), b?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
