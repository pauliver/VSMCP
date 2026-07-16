using System.Collections.Generic;
using System.IO;
using VSMCP.Shared;

namespace VSMCP.Core;

/// <summary>
/// Confines mutating file operations (file.write, file.replace_range, scaffold/create_class,
/// file.move_many, edit.move_type/move_method) to an allowed set of roots. Without this the tool
/// surface is an arbitrary-overwrite primitive: an absolute path handed to file.write is written
/// with no check that it is inside the open solution or repo (audit through-line: data safety).
///
/// Pure and VS-independent so it is unit-testable with plain <c>dotnet test</c>; the VSIX computes
/// the allowed roots (the solution's enclosing git repo root, else the solution dir) and calls
/// <see cref="EnsureWithinAnyRoot"/> from every mutating RpcTarget method.
///
/// Matching is by normalized full path with a trailing-separator boundary, so a candidate under
/// <c>C:\RepoEvil</c> is NOT accepted for the root <c>C:\Repo</c> (sibling-prefix bypass).
/// </summary>
public static class WriteScopePolicy
{
    // Windows-only product (VS 2022); paths are case-insensitive.
    private const System.StringComparison Cmp = System.StringComparison.OrdinalIgnoreCase;

    /// <summary>True when <paramref name="candidate"/> resolves to <paramref name="root"/> itself or a descendant of it.</summary>
    public static bool IsWithinRoot(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
            return false;

        string fullRoot, fullCandidate;
        try
        {
            fullRoot = TrimTrailingSeparators(Path.GetFullPath(root));
            fullCandidate = TrimTrailingSeparators(Path.GetFullPath(candidate));
        }
        catch
        {
            // Malformed path (illegal chars, etc.) is never "within" — fail closed.
            return false;
        }

        if (string.Equals(fullRoot, fullCandidate, Cmp))
            return true;

        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, Cmp);
    }

    /// <summary>True when <paramref name="candidate"/> is within any of the <paramref name="roots"/>.</summary>
    public static bool IsWithinAnyRoot(IEnumerable<string> roots, string candidate)
    {
        if (roots is null) return false;
        foreach (var root in roots)
            if (IsWithinRoot(root, candidate))
                return true;
        return false;
    }

    /// <summary>
    /// Returns the normalized full path when <paramref name="candidate"/> is within an allowed root,
    /// otherwise throws <see cref="VsmcpException"/> (<see cref="ErrorCodes.WrongState"/>) naming the
    /// operation — so a blocked write surfaces an actionable reason, not a silent success or a raw IO throw.
    /// </summary>
    public static string EnsureWithinAnyRoot(IEnumerable<string> roots, string candidate, string operation)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new VsmcpException(ErrorCodes.WrongState, $"{operation}: no path was provided.");

        if (!IsWithinAnyRoot(roots, candidate))
            throw new VsmcpException(ErrorCodes.WrongState,
                $"{operation}: path '{candidate}' is outside the allowed write roots. " +
                "VSMCP confines writes to the open solution's repository. Move the target inside it, " +
                "or widen the roots via \"writeScopeRoots\" in %LOCALAPPDATA%\\VSMCP\\config.json.");

        return Path.GetFullPath(candidate);
    }

    private static string TrimTrailingSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
