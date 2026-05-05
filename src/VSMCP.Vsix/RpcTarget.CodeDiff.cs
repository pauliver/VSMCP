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

namespace VSMCP.Vsix
{
    internal sealed partial class RpcTarget
    {
        public async Task<CodeDiffResult> CodeDiffAsync(string file, string? baseHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(file))
                throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
            var current = await FileReadAsync(file, null, cancellationToken).ConfigureAwait(false);
            var toHash = CtxHelpers.Sha1Hex(current.Content);
            // Try git first (most useful when called without baseHash).
            if (string.IsNullOrEmpty(baseHash))
            {
                var(ok, fromText, fromHash) = TryReadGitHead(file);
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
                        new()
                        {
                            StartLine = 1,
                            AddedLines = current.Content.Replace("\r\n", "\n").Split('\n').ToList()
                        }
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
                Hunks = string.Equals(baseHash, toHash, StringComparison.Ordinal) ? new List<DiffHunk>() : new List<DiffHunk>
                {
                    new()
                    {
                        StartLine = 1,
                        AddedLines = new List<string>
                        {
                            "<baseHash known but content not cached server-side; pass baseHash=null to diff against git HEAD>"
                        }
                    }
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
    }
}