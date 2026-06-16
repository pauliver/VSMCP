using System;
using System.Collections.Generic;
using VSMCP.Shared;

namespace VSMCP.Core;

/// <summary>
/// A solution-wide, in-memory index of syntactic C++ declarations (#141), keyed by file so a
/// single file can be re-parsed and replaced cheaply on save. Lets symbol lookups hit a cache
/// instead of re-parsing the whole solution per query. Pure and thread-safe (internal lock),
/// so it is unit-testable without VS; the VSIX populates it from CppOutlineParser output.
/// </summary>
public sealed class CppSymbolIndex
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<CppDecl>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private int _symbolCount;

    public int FileCount { get { lock (_lock) return _byFile.Count; } }
    public int SymbolCount { get { lock (_lock) return _symbolCount; } }

    public void AddOrReplaceFile(string file, IReadOnlyList<CppDecl> decls)
    {
        lock (_lock)
        {
            if (_byFile.TryGetValue(file, out var old)) _symbolCount -= old.Count;
            var copy = new List<CppDecl>(decls);
            _byFile[file] = copy;
            _symbolCount += copy.Count;
        }
    }

    public bool RemoveFile(string file)
    {
        lock (_lock)
        {
            if (!_byFile.TryGetValue(file, out var old)) return false;
            _symbolCount -= old.Count;
            return _byFile.Remove(file);
        }
    }

    public void Clear()
    {
        lock (_lock) { _byFile.Clear(); _symbolCount = 0; }
    }

    /// <summary>
    /// Case-insensitive substring match on declaration name, optionally filtered by kind.
    /// Returns up to <paramref name="max"/> entries; <paramref name="truncated"/> reports whether
    /// more matched.
    /// </summary>
    public IReadOnlyList<CppIndexEntry> Find(string name, string? kind, int max, out bool truncated)
    {
        truncated = false;
        var results = new List<CppIndexEntry>();
        if (max <= 0) max = 200;
        name ??= "";
        lock (_lock)
        {
            foreach (var kv in _byFile)
            {
                foreach (var d in kv.Value)
                {
                    if (kind is not null && !string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Length > 0 && d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (results.Count >= max) { truncated = true; return results; }
                    results.Add(new CppIndexEntry
                    {
                        File = kv.Key,
                        Name = d.Name,
                        Kind = d.Kind,
                        Container = d.Container,
                        Line = d.Line,
                    });
                }
            }
        }
        return results;
    }
}
