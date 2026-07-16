using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    private const string MsbuildLegacyNs = "http://schemas.microsoft.com/developer/msbuild/2003";

    /// <summary>
    /// Bulk rename/move files on disk and (when <paramref name="updateProject"/> is true) update
    /// any owning .csproj's <c>&lt;Compile Include&gt;</c> entry. Fail-fast: if any pair is
    /// rejected during validation, NO disk changes are made (validation runs before mutation).
    /// On per-pair mutation failure after the disk move, that pair's move and csproj edit are
    /// rolled back and the failure is reported in the outcome's <c>Error</c> — pairs already
    /// completed keep their results and later pairs still run (partial result, not all-or-nothing).
    /// </summary>
    public async Task<FileMoveManyResult> FileMoveManyAsync(
        IReadOnlyList<FileMovePair> moves,
        bool updateProject,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        var result = new FileMoveManyResult { DryRun = dryRun };

        if (moves is null || moves.Count == 0)
            return result;

        // ---- Pass 1: validate all pairs without touching disk. Fail-fast. ----
        var plan = new List<MovePlan>(moves.Count);
        foreach (var m in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.Add(ValidatePair(m, updateProject));
        }

        // Every path this batch will touch must be inside the allowed write roots — including
        // destinations and any .csproj that gets edited. Still part of fail-fast validation.
        foreach (var p in plan)
        {
            if (p.Skipped is not null) continue;
            await EnsureWriteAllowedAsync(p.FromAbs, "file.move_many", cancellationToken).ConfigureAwait(false);
            await EnsureWriteAllowedAsync(p.ToAbs, "file.move_many", cancellationToken).ConfigureAwait(false);
            if (p.WillEditProject && p.ProjectPath is not null)
                await EnsureWriteAllowedAsync(p.ProjectPath, "file.move_many", cancellationToken).ConfigureAwait(false);
        }

        // ---- Pass 2: execute. In dry-run, just record the plan as outcomes. ----
        foreach (var p in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = new FileMoveOutcome
            {
                From = p.FromAbs,
                To = p.ToAbs,
                ProjectPath = p.ProjectPath,
                Skipped = p.Skipped,
            };

            if (p.Skipped is not null)
            {
                result.Outcomes.Add(outcome);
                continue;
            }

            if (dryRun)
            {
                // Report intent only.
                outcome.Moved = false;
                outcome.ProjectUpdated = p.WillEditProject;
                result.Outcomes.Add(outcome);
                continue;
            }

            // Real mutation. Capture rollback state.
            byte[]? savedCsprojBytes = null;

            try
            {
                // Ensure destination directory exists.
                var destDir = Path.GetDirectoryName(p.ToAbs);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                // Case-only rename guard (Windows volumes are case-insensitive).
                if (string.Equals(p.FromAbs, p.ToAbs, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(p.FromAbs, p.ToAbs, StringComparison.Ordinal))
                {
                    var tmp = p.FromAbs + ".vsmcp_caserename_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    File.Move(p.FromAbs, tmp);
                    File.Move(tmp, p.ToAbs);
                }
                else
                {
                    File.Move(p.FromAbs, p.ToAbs);
                }
                outcome.Moved = true;

                // csproj sync.
                if (p.WillEditProject && p.ProjectPath is not null)
                {
                    savedCsprojBytes = File.ReadAllBytes(p.ProjectPath);
                    var edited = TryUpdateCompileInclude(p.ProjectPath, p.FromAbs, p.ToAbs);
                    outcome.ProjectUpdated = edited;
                    if (!edited)
                    {
                        // The .csproj didn't list this file (could be auto-globbed or unrelated).
                        // Not a failure; just record that no edit was applied.
                        outcome.ProjectPath = p.ProjectPath;
                    }
                }

                result.Outcomes.Add(outcome);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Rollback this pair's mutations, record the failure, and keep going —
                // outcomes from pairs that already completed must survive.
                if (outcome.Moved && File.Exists(p.ToAbs) && !File.Exists(p.FromAbs))
                {
                    try { File.Move(p.ToAbs, p.FromAbs); outcome.Moved = false; } catch { }
                }
                if (savedCsprojBytes is not null && p.ProjectPath is not null)
                {
                    try { File.WriteAllBytes(p.ProjectPath, savedCsprojBytes); outcome.ProjectUpdated = false; } catch { }
                }
                outcome.Error = ex.Message;
                result.Outcomes.Add(outcome);
            }
        }

        return result;
    }

    private sealed class MovePlan
    {
        public string FromAbs = "";
        public string ToAbs = "";
        public string? ProjectPath;     // owning .csproj for `from`, or null
        public bool WillEditProject;    // updateProject AND project has explicit <Compile Include> for `from`
        public string? Skipped;         // non-null => this pair is a no-op with reason
    }

    private static MovePlan ValidatePair(FileMovePair m, bool updateProject)
    {
        if (m is null) throw new VsmcpException(ErrorCodes.NotFound, "move pair is null.");
        if (string.IsNullOrWhiteSpace(m.From)) throw new VsmcpException(ErrorCodes.NotFound, "from is required.");
        if (string.IsNullOrWhiteSpace(m.To)) throw new VsmcpException(ErrorCodes.NotFound, "to is required.");

        var fromAbs = Path.GetFullPath(m.From);
        var toAbs = Path.GetFullPath(m.To);

        if (!File.Exists(fromAbs))
            throw new VsmcpException(ErrorCodes.NotFound, $"from not found: {fromAbs}");

        // Same-path no-op (after normalization).
        if (string.Equals(fromAbs, toAbs, StringComparison.Ordinal))
            return new MovePlan { FromAbs = fromAbs, ToAbs = toAbs, Skipped = "from and to are the same path" };

        // Case-only rename is allowed (handled in the mutation phase).
        var caseOnly = string.Equals(fromAbs, toAbs, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && File.Exists(toAbs))
            throw new VsmcpException(ErrorCodes.WrongState, $"to already exists: {toAbs}");

        var fromProj = FindOwningCsproj(fromAbs);
        var toProj = FindOwningCsproj(toAbs);

        // Cross-project moves are out of scope for v1: namespace + InternalsVisibleTo + reference
        // implications need manual review.
        if (fromProj is not null && toProj is not null
            && !string.Equals(fromProj, toProj, StringComparison.OrdinalIgnoreCase))
        {
            throw new VsmcpException(ErrorCodes.Unsupported,
                $"cross-project move rejected: from is in '{fromProj}', to is in '{toProj}'.");
        }
        if (fromProj is null && toProj is not null)
        {
            throw new VsmcpException(ErrorCodes.Unsupported,
                $"cross-project move rejected: from has no owning .csproj, to is in '{toProj}'.");
        }

        var willEdit = false;
        if (updateProject && fromProj is not null)
            willEdit = ProjectListsCompileInclude(fromProj, fromAbs);

        return new MovePlan
        {
            FromAbs = fromAbs,
            ToAbs = toAbs,
            ProjectPath = fromProj,
            WillEditProject = willEdit,
        };
    }

    /// <summary>Walk up from a path looking for a sibling .csproj.</summary>
    private static string? FindOwningCsproj(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        for (int i = 0; i < 32 && !string.IsNullOrEmpty(dir); i++)
        {
            string[] csprojs;
            try { csprojs = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly); }
            catch { csprojs = Array.Empty<string>(); }
            if (csprojs.Length > 0) return csprojs[0];
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.Ordinal)) break;
            dir = parent;
        }
        return null;
    }

    private static bool ProjectListsCompileInclude(string csprojPath, string fileAbs)
    {
        var (doc, _, _) = LoadCsproj(csprojPath);
        var ns = doc.Root?.Name.NamespaceName ?? "";
        var compileName = string.IsNullOrEmpty(ns) ? (XName)"Compile" : XName.Get("Compile", ns);

        var projDir = Path.GetDirectoryName(csprojPath)!;
        var rel = MakeRelative(projDir, fileAbs);

        return doc.Descendants(compileName)
            .Any(c => RelativePathsEqual(c.Attribute("Include")?.Value, rel));
    }

    /// <summary>
    /// Update a single &lt;Compile Include="rel(from)"&gt; entry to rel(to). Returns true if an
    /// entry was found and edited; false if no matching entry exists (e.g. SDK-style csproj
    /// with auto-glob, or the file was never explicitly listed).
    /// </summary>
    private static bool TryUpdateCompileInclude(string csprojPath, string fromAbs, string toAbs)
    {
        var (doc, encoding, lineEnding) = LoadCsproj(csprojPath);
        var ns = doc.Root?.Name.NamespaceName ?? "";
        var compileName = string.IsNullOrEmpty(ns) ? (XName)"Compile" : XName.Get("Compile", ns);

        var projDir = Path.GetDirectoryName(csprojPath)!;
        var fromRel = MakeRelative(projDir, fromAbs);
        var toRel = MakeRelative(projDir, toAbs);

        bool any = false;
        foreach (var c in doc.Descendants(compileName).ToList())
        {
            var inc = c.Attribute("Include");
            if (inc is null) continue;
            if (!RelativePathsEqual(inc.Value, fromRel)) continue;
            inc.Value = toRel;
            any = true;
        }

        if (!any) return false;

        SaveCsproj(csprojPath, doc, encoding, lineEnding);
        return true;
    }

    private static (XDocument Doc, Encoding Encoding, string LineEnding) LoadCsproj(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // Detect BOM (most VSIX legacy csprojs are UTF-8 with BOM).
        Encoding encoding = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Detect line endings (look at the first newline).
        string lineEnding = "\r\n";
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                lineEnding = i > 0 && bytes[i - 1] == (byte)'\r' ? "\r\n" : "\n";
                break;
            }
        }

        // PreserveWhitespace keeps existing inter-element whitespace so SaveCsproj is non-reformatting.
        var preambleLen = encoding.GetPreamble().Length;
        var doc = XDocument.Parse(encoding.GetString(bytes, preambleLen, bytes.Length - preambleLen),
                                  LoadOptions.PreserveWhitespace);
        return (doc, encoding, lineEnding);
    }

    private static void SaveCsproj(string path, XDocument doc, Encoding encoding, string lineEnding)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = encoding,
            NewLineChars = lineEnding,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
            Indent = false, // PreserveWhitespace handles it
        };

        using var fs = File.Create(path);
        using var w = XmlWriter.Create(fs, settings);
        doc.Save(w);
    }

    private static string MakeRelative(string baseDir, string fullPath)
    {
        var baseUri = new Uri(EnsureTrailingSeparator(baseDir));
        var fullUri = new Uri(fullPath);
        var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
        // .csproj uses backslashes in Include attributes by convention.
        return rel.Replace('/', '\\');
    }

    private static string EnsureTrailingSeparator(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return dir;
        var last = dir[dir.Length - 1];
        if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return dir;
        return dir + Path.DirectorySeparatorChar;
    }

    private static bool RelativePathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }
}
