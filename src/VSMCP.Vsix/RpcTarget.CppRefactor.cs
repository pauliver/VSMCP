using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // ---- cpp_rename_solution ----

    public async Task<CppRenameSolutionResult> CppRenameSolutionAsync(string file, int line, int column, string newName, int maxFiles, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(newName)) throw new VsmcpException(ErrorCodes.NotFound, "newName is required.");
        if (maxFiles <= 0) maxFiles = 200;

        // Use cpp_find_references_solution to find all callsites across TUs.
        var refs = await CppFindReferencesSolutionAsync(file, line, column, maxFiles, null, null, cancellationToken).ConfigureAwait(false);
        if (refs.Locations.Count == 0)
            return new CppRenameSolutionResult { OldName = refs.Spelling ?? "", NewName = newName };

        var oldName = refs.Spelling ?? "";
        if (string.IsNullOrEmpty(oldName))
            throw new VsmcpException(ErrorCodes.WrongState, "Could not determine the old name from the cursor.");

        // Group by file to coordinate edits per-file (bottom-up within each file).
        var byFile = refs.Locations
            .GroupBy(l => l.File ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        var edited = new List<CppLocation>();
        int filesEdited = 0;
        foreach (var group in byFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = group
                .OrderByDescending(l => l.Line)
                .ThenByDescending(l => l.Column)
                .ToList();
            foreach (var loc in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FileReplaceRangeAsync(loc.File, new FileRange
                {
                    StartLine = loc.Line,
                    StartColumn = loc.Column,
                    EndLine = loc.Line,
                    EndColumn = loc.Column + oldName.Length,
                }, newName, cancellationToken).ConfigureAwait(false);
                edited.Add(loc);
            }
            try { await CppInvalidateAsync(group.Key, cancellationToken).ConfigureAwait(false); } catch { }
            filesEdited++;
        }

        return new CppRenameSolutionResult
        {
            OldName = oldName,
            NewName = newName,
            EditedLocations = edited,
            FilesEdited = filesEdited,
            TotalReferences = refs.Total,
        };
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

        try
        {
            await CppInvalidateAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            await CppInvalidateAsync(targetFile, cancellationToken).ConfigureAwait(false);
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
            Note = "v1: header-only move. If this type has out-of-line method definitions in a .cpp, those references stay in the original .cpp and may need manual #include adjustments.",
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

        try
        {
            await CppInvalidateAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            await CppInvalidateAsync(targetFile, cancellationToken).ConfigureAwait(false);
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
            Note = "v1: text move with className:: qualification injected when missing. If the method body references private members, you may need to add a friend declaration or move to the .cpp instead.",
        };
    }
}
