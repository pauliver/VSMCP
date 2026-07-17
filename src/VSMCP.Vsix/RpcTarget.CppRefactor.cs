using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // ---- cpp_rename_solution ----

    public async Task<CppRenameSolutionResult> CppRenameSolutionAsync(string file, int line, int column, string newName, int maxFiles, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(newName)) throw new VsmcpException(ErrorCodes.NotFound, "newName is required.");
        if (maxFiles <= 0) maxFiles = 200;

        // Use cpp_find_references_solution to find all callsites across TUs.
        var refs = await CppFindReferencesSolutionAsync(file, line, column, maxFiles, null, null, cancellationToken).ConfigureAwait(false);

        var result = new CppRenameSolutionResult
        {
            OldName = refs.Spelling ?? "",
            NewName = newName,
            DryRun = dryRun,
            Truncated = refs.Truncated,
            TotalReferences = refs.Total,
        };
        if (refs.Locations.Count == 0)
            return result;

        var oldName = refs.Spelling ?? "";
        if (string.IsNullOrEmpty(oldName))
            throw new VsmcpException(ErrorCodes.WrongState, "Could not determine the old name from the cursor.");

        var roots = await GetWriteRootsAsync(cancellationToken).ConfigureAwait(false);

        // Group by file to coordinate edits per-file (bottom-up within each file).
        var byFile = refs.Locations
            .GroupBy(l => l.File ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var group in byFile)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Never splice files outside the solution/repo write roots — libclang reports
            // references inside system and SDK headers too, and rewriting those is catastrophic.
            var target = Path.GetFullPath(group.Key);
            if (roots.Count > 0 && !WriteScopePolicy.IsWithinAnyRoot(roots, target))
            {
                result.SkippedOutOfScope += group.Count();
                continue;
            }

            var (applied, skipped) = await SpliceRenameFileAsync(
                target, group, oldName, newName, dryRun, result.EditedLocations, cancellationToken).ConfigureAwait(false);
            result.SkippedMismatched += skipped;

            if (applied > 0)
            {
                if (!dryRun)
                {
                    try { await CppInvalidateAsync(target, cancellationToken).ConfigureAwait(false); } catch { }
                }
                result.FilesEdited++;
            }
        }

        return result;
    }

    /// <summary>
    /// Shared splice engine for cpp_rename / cpp_rename_solution: re-verifies every site against
    /// the file's CURRENT content (a mismatch is skipped, never spliced), converts libclang's
    /// UTF-8 byte columns to char columns so non-ASCII lines splice correctly, and edits
    /// bottom-up so earlier edits don't shift later ones. Returns (applied, skippedMismatched);
    /// verified sites are appended to <paramref name="editedOut"/> even in dry runs (the plan).
    /// </summary>
    private async Task<(int Applied, int Skipped)> SpliceRenameFileAsync(
        string file, IEnumerable<CppLocation> locs, string oldName, string newName,
        bool dryRun, List<CppLocation>? editedOut, CancellationToken ct)
    {
        string[] lines;
        try
        {
            lines = (await FileReadAsync(file, null, ct).ConfigureAwait(false)).Content.Split('\n');
        }
        catch
        {
            return (0, locs.Count());
        }

        int applied = 0, skipped = 0;
        foreach (var loc in locs.OrderByDescending(l => l.Line).ThenByDescending(l => l.Column))
        {
            ct.ThrowIfCancellationRequested();
            var charCol = SpliceCharColumn(lines, loc.Line, loc.Column, oldName);
            if (charCol < 0)
            {
                skipped++;
                continue;
            }

            if (!dryRun)
            {
                await FileReplaceRangeAsync(file, new FileRange
                {
                    StartLine = loc.Line,
                    StartColumn = charCol,
                    EndLine = loc.Line,
                    EndColumn = charCol + oldName.Length,
                }, newName, ct).ConfigureAwait(false);
            }
            editedOut?.Add(loc);
            applied++;
        }
        return (applied, skipped);
    }

    /// <summary>1-based CHAR column where <paramref name="oldName"/> verifiably sits at the
    /// 1-based (line, byte-column) site reported by libclang, or -1 on any mismatch.</summary>
    private static int SpliceCharColumn(string[] lines, int line, int byteColumn, string oldName)
    {
        var li = line - 1;
        if (li < 0 || li >= lines.Length) return -1;
        var text = lines[li].TrimEnd('\r');
        var ci = TextColumns.Utf8ByteToCharIndex(text, byteColumn - 1);
        if (ci < 0 || ci + oldName.Length > text.Length) return -1;
        if (string.CompareOrdinal(text, ci, oldName, 0, oldName.Length) != 0) return -1;
        return ci + 1;
    }

    /// <summary>
    /// After a header type/method moves from <paramref name="sourceHeader"/> to <paramref name="targetHeader"/>,
    /// scan sister .cpp files (basename match in same dir + solution-wide) for an
    /// <c>#include "OldHeaderName"</c> that should now point at the new header. Adds the new
    /// include alongside the old one when the .cpp also references <paramref name="className"/>::
    /// (so we don't break files that incidentally include the source header for unrelated reasons).
    /// Returns the list of sibling files we touched.
    /// </summary>
    private async Task<List<string>> UpdateSiblingIncludesAsync(string sourceHeader, string targetHeader, string className, CancellationToken ct)
    {
        var updated = new List<string>();
        var srcBase = Path.GetFileName(sourceHeader);
        var tgtBase = Path.GetFileName(targetHeader);
        if (string.Equals(srcBase, tgtBase, StringComparison.OrdinalIgnoreCase)) return updated;

        // Candidate set: sibling in same directory + any .cpp the solution exposes.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var srcDir = Path.GetDirectoryName(sourceHeader);
        if (!string.IsNullOrEmpty(srcDir))
        {
            foreach (var ext in new[] { ".cpp", ".cc", ".cxx", ".c" })
            {
                var sister = Path.ChangeExtension(sourceHeader, ext);
                if (File.Exists(sister)) candidates.Add(sister);
            }
        }

        // Also pull .cpp files known to the solution.
        try
        {
            var files = await FileListAsync(null, null, "*.{cpp,cc,cxx,c}",
                new[] { "file" }, 50_000, ct).ConfigureAwait(false);
            foreach (var f in files.Files) candidates.Add(f.Path);
        }
        catch { /* best-effort */ }

        var includeRx = new System.Text.RegularExpressions.Regex(
            $@"^\s*#\s*include\s*[""<]([^"">]*?{System.Text.RegularExpressions.Regex.Escape(srcBase)})[""<>]",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
        var classQualifiedRx = new System.Text.RegularExpressions.Regex(
            $@"\b{System.Text.RegularExpressions.Regex.Escape(className)}\s*::",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var cand in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Best-effort bulk rewrite: silently skip candidates outside the write roots
            // rather than aborting the whole move.
            if (!await IsWriteAllowedAsync(Path.GetFullPath(cand), ct).ConfigureAwait(false)) continue;
            string text;
            try { text = File.ReadAllText(cand); } catch { continue; }
            if (!includeRx.IsMatch(text)) continue;
            // Heuristic: only rewrite if the file actually uses ClassName:: (out-of-line definitions).
            if (!classQualifiedRx.IsMatch(text)) continue;
            // Don't double-add the new include if it's already there.
            var hasTarget = new System.Text.RegularExpressions.Regex(
                $@"^\s*#\s*include\s*[""<][^"">]*?{System.Text.RegularExpressions.Regex.Escape(tgtBase)}[""<>]",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
            if (hasTarget.IsMatch(text)) { updated.Add(cand); continue; }

            // Inject `#include "tgtBase"` immediately after the matched source include line.
            var rewritten = includeRx.Replace(text, m =>
            {
                var path = m.Groups[1].Value;
                // Preserve the original include style (quoted vs angle).
                var line = m.Value;
                var isAngle = line.Contains('<');
                var newLine = isAngle ? $"#include <{tgtBase}>" : $"#include \"{tgtBase}\"";
                return line + Environment.NewLine + newLine;
            }, 1);

            try
            {
                File.WriteAllText(cand, rewritten);
                updated.Add(cand);
            }
            catch { /* file locked or read-only — skip */ }
        }
        return updated;
    }

    // ---- cpp_move_type ----

    public async Task<CppMoveTypeResult> CppMoveTypeAsync(string sourceFile, string typeName, string targetFile, bool createTargetIfMissing, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourceFile)) throw new VsmcpException(ErrorCodes.NotFound, "sourceFile is required.");
        if (string.IsNullOrEmpty(typeName)) throw new VsmcpException(ErrorCodes.NotFound, "typeName is required.");
        if (string.IsNullOrEmpty(targetFile)) throw new VsmcpException(ErrorCodes.NotFound, "targetFile is required.");
        await Task.Yield();

        var outline = CppOutlineParser.Parse(sourceFile);
        var typeDecl = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "class" || d.Kind == "struct" || d.Kind == "union" || d.Kind == "enum") && d.Name == typeName);
        if (typeDecl is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Type '{typeName}' not found in '{sourceFile}'.");

        var lines = File.ReadAllLines(sourceFile);
        int startIdx = typeDecl.Line - 1;
        int endIdx = FindMemberEndLine(lines, startIdx);

        // Capture the type body INCLUDING the trailing `;` if present (common for class declarations).
        // For "class Foo { ... };" the closing line ends with `};` — we want the whole thing.
        var bodyLines = new List<string>();
        for (int i = startIdx; i <= endIdx && i < lines.Length; i++)
        {
            bodyLines.Add(lines[i]);
        }
        // If the line right after endIdx starts with `;` only, include it too.
        if (endIdx + 1 < lines.Length && lines[endIdx + 1].TrimStart().StartsWith(";"))
        {
            endIdx += 1;
        }
        // If the close-brace line ends with `}` but no `;`, the next non-empty line might be just `;`.
        // Otherwise the `;` is on the same line ("};") — already captured.

        var body = string.Join(Environment.NewLine, bodyLines);

        // Append to (or create) targetFile.
        await EnsureWriteAllowedAsync(Path.GetFullPath(targetFile), "cpp.move_type", cancellationToken).ConfigureAwait(false);
        if (!File.Exists(targetFile))
        {
            if (!createTargetIfMissing)
                throw new VsmcpException(ErrorCodes.NotFound, $"Target file does not exist: {targetFile}. Set createTargetIfMissing=true to create it.");
            var dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            // Seed with `#pragma once` and any namespace from source for convenience.
            var seed = new StringBuilder();
            seed.Append("#pragma once").Append(Environment.NewLine).Append(Environment.NewLine);
            seed.Append(body).Append(Environment.NewLine);
            File.WriteAllText(targetFile, seed.ToString());
        }
        else
        {
            File.AppendAllText(targetFile, Environment.NewLine + body + Environment.NewLine);
        }

        // Remove the type from the source file.
        await FileReplaceRangeAsync(sourceFile, new FileRange
        {
            StartLine = startIdx + 1,
            StartColumn = 1,
            EndLine = endIdx + 1,
            EndColumn = lines[Math.Min(endIdx, lines.Length - 1)].Length + 1,
        }, "", cancellationToken).ConfigureAwait(false);

        // Follow .cpp out-of-line definitions: any sister .cpp using `TypeName::` and including
        // the source header gets a parallel #include of the target header.
        List<string> updatedSiblings;
        try { updatedSiblings = await UpdateSiblingIncludesAsync(sourceFile, targetFile, typeName, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { VsmcpLog.Debug("cpp.move", $"UpdateSiblingIncludesAsync({sourceFile})", ex); updatedSiblings = new List<string>(); }

        try
        {
            await CppInvalidateAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            await CppInvalidateAsync(targetFile, cancellationToken).ConfigureAwait(false);
            foreach (var s in updatedSiblings) await CppInvalidateAsync(s, cancellationToken).ConfigureAwait(false);
        }
        catch { }

        return new CppMoveTypeResult
        {
            SourceFile = sourceFile,
            TargetFile = targetFile,
            TypeName = typeName,
            Moved = true,
            StartLine = startIdx + 1,
            EndLine = endIdx + 1,
            UpdatedSiblingFiles = updatedSiblings,
            Note = updatedSiblings.Count > 0
                ? $"Moved type and added #include of target header to {updatedSiblings.Count} sibling .cpp file(s) that reference {typeName}::."
                : "v1: header-only move. No sibling .cpp files needed include rewrites.",
        };
    }

    // ---- cpp_move_method ----

    public async Task<CppMoveMethodResult> CppMoveMethodAsync(string sourceFile, string className, string methodName, string targetFile, bool createTargetIfMissing, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourceFile)) throw new VsmcpException(ErrorCodes.NotFound, "sourceFile is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(methodName)) throw new VsmcpException(ErrorCodes.NotFound, "methodName is required.");
        if (string.IsNullOrEmpty(targetFile)) throw new VsmcpException(ErrorCodes.NotFound, "targetFile is required.");
        await Task.Yield();

        var outline = CppOutlineParser.Parse(sourceFile);
        var hit = outline.Declarations.FirstOrDefault(d =>
            (d.Kind == "method" || d.Kind == "function")
            && d.Name == methodName
            && d.Container is not null
            && (d.Container == className || d.Container.EndsWith("::" + className, StringComparison.Ordinal)));
        if (hit is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"Method '{className}::{methodName}' not found in '{sourceFile}'.");

        var lines = File.ReadAllLines(sourceFile);
        int startIdx = hit.Line - 1;
        int endIdx = FindMemberEndLine(lines, startIdx);

        var sb = new StringBuilder();
        for (int i = startIdx; i <= endIdx && i < lines.Length; i++) sb.AppendLine(lines[i]);
        var body = sb.ToString();

        // Wrap with the qualified name when appending to targetFile so it builds as an out-of-line def.
        // Rough heuristic: if the body contains `methodName(` at the start (not `className::methodName(`),
        // splice in `className::` before the method name. Best-effort.
        var qualified = body;
        var classQualifyRx = new System.Text.RegularExpressions.Regex(
            $@"\b{System.Text.RegularExpressions.Regex.Escape(methodName)}\s*\(",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        if (!body.Contains(className + "::" + methodName))
        {
            qualified = classQualifyRx.Replace(body, className + "::" + methodName + "(", 1);
        }

        await EnsureWriteAllowedAsync(Path.GetFullPath(targetFile), "cpp.move_method", cancellationToken).ConfigureAwait(false);
        if (!File.Exists(targetFile))
        {
            if (!createTargetIfMissing)
                throw new VsmcpException(ErrorCodes.NotFound, $"Target file does not exist: {targetFile}. Set createTargetIfMissing=true to create it.");
            var dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(targetFile, qualified);
        }
        else
        {
            File.AppendAllText(targetFile, Environment.NewLine + qualified);
        }

        await FileReplaceRangeAsync(sourceFile, new FileRange
        {
            StartLine = startIdx + 1,
            StartColumn = 1,
            EndLine = endIdx + 1,
            EndColumn = lines[Math.Min(endIdx, lines.Length - 1)].Length + 1,
        }, "", cancellationToken).ConfigureAwait(false);

        List<string> updatedSiblings;
        try { updatedSiblings = await UpdateSiblingIncludesAsync(sourceFile, targetFile, className, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { VsmcpLog.Debug("cpp.move", $"UpdateSiblingIncludesAsync({sourceFile}/{className})", ex); updatedSiblings = new List<string>(); }

        try
        {
            await CppInvalidateAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            await CppInvalidateAsync(targetFile, cancellationToken).ConfigureAwait(false);
            foreach (var s in updatedSiblings) await CppInvalidateAsync(s, cancellationToken).ConfigureAwait(false);
        }
        catch { }

        return new CppMoveMethodResult
        {
            SourceFile = sourceFile,
            TargetFile = targetFile,
            ClassName = className,
            MethodName = methodName,
            Moved = true,
            StartLine = startIdx + 1,
            EndLine = endIdx + 1,
            UpdatedSiblingFiles = updatedSiblings,
            Note = updatedSiblings.Count > 0
                ? $"Moved method and updated {updatedSiblings.Count} sibling .cpp file(s) that reference {className}::."
                : "v1: text move with className:: qualification injected when missing.",
        };
    }
}
