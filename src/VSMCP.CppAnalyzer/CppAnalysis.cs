using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ClangSharp.Interop;
using VSMCP.Shared;

namespace VSMCP.CppAnalyzer;

/// <summary>
/// libclang-backed semantic analysis. Maintains a global CXIndex and a per-file
/// CXTranslationUnit cache. Each TU is single-threaded; an LRU cap keeps memory
/// bounded.
///
/// Phase F-2 ships <see cref="Diagnostics"/>; Phase F-3 ships find_references /
/// quick_info / goto_definition. Phase F-4 wires reparse on invalidate.
/// </summary>
internal sealed class CppAnalysis : IDisposable
{
    private readonly CXIndex _index;
    // LRU cache: dict for O(1) lookup, linked list for O(1) reorder. Most-recently-used
    // entries are at the head; eviction takes from the tail.
    private readonly Dictionary<string, LinkedListNode<CachedTu>> _tus = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CachedTu> _lru = new();
    private readonly object _lock = new();
    private const int MaxCachedTus = 50;

    public CppAnalysis()
    {
        _index = CXIndex.Create();
    }

    public CppDiagnosticsResult Diagnostics(string file, string[]? extraIncludes, string[]? extraDefines, CancellationToken ct)
    {
        var result = new CppDiagnosticsResult { File = file };
        if (!File.Exists(file))
        {
            result.Diagnostics.Add(new CppDiagnostic
            {
                Severity = CppDiagnosticSeverity.Error,
                Message = $"File not found: {file}",
            });
            result.HasErrors = true;
            return result;
        }

        using var entry = AcquireTu(file, extraIncludes, extraDefines);
        if (entry.Tu.Handle == IntPtr.Zero)
        {
            result.Diagnostics.Add(new CppDiagnostic
            {
                Severity = CppDiagnosticSeverity.Fatal,
                Message = "libclang failed to parse the translation unit.",
            });
            result.HasErrors = true;
            return result;
        }

        var n = entry.Tu.NumDiagnostics;
        for (uint i = 0; i < n; i++)
        {
            using var d = entry.Tu.GetDiagnostic(i);
            var sev = (CppDiagnosticSeverity)(int)d.Severity;
            var loc = ToLocation(d.Location);
            result.Diagnostics.Add(new CppDiagnostic
            {
                Severity = sev,
                Message = d.Spelling.CString.ToString() ?? "",
                Location = loc,
                Category = d.CategoryText.CString.ToString(),
            });
            if (sev >= CppDiagnosticSeverity.Error) result.HasErrors = true;
        }
        return result;
    }

    public CppLocationListResult FindReferences(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken ct)
    {
        var result = new CppLocationListResult();
        if (!File.Exists(file)) return result;

        using var entry = AcquireTu(file, extraIncludes, extraDefines);
        if (entry.Tu.Handle == IntPtr.Zero) return result;

        var clangFile = entry.Tu.GetFile(file);
        var loc = entry.Tu.GetLocation(clangFile, (uint)line, (uint)column);
        var cursor = entry.Tu.GetCursor(loc);
        var canonical = cursor.CanonicalCursor;
        if (canonical.IsNull) return result;

        result.Spelling = canonical.Spelling.CString.ToString();
        result.Kind = canonical.Kind.ToString();

        // Walk the TU collecting references that resolve to the same canonical cursor.
        var rootCanonicalUsr = canonical.Usr.CString.ToString();
        var matches = WalkForReferences(entry.Tu, rootCanonicalUsr);

        // Also include the declaration site itself.
        var decLoc = ToLocation(canonical.Location);
        if (decLoc is not null) matches.Insert(0, decLoc);

        // De-duplicate by file+line+col.
        var seen = new HashSet<string>();
        foreach (var m in matches)
        {
            var key = $"{m.File}|{m.Line}|{m.Column}";
            if (seen.Add(key)) result.Locations.Add(m);
        }
        result.Total = result.Locations.Count;
        return result;
    }

    public CppQuickInfoResult QuickInfo(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken ct)
    {
        var result = new CppQuickInfoResult();
        if (!File.Exists(file)) return result;

        using var entry = AcquireTu(file, extraIncludes, extraDefines);
        if (entry.Tu.Handle == IntPtr.Zero) return result;

        var clangFile = entry.Tu.GetFile(file);
        var loc = entry.Tu.GetLocation(clangFile, (uint)line, (uint)column);
        var cursor = entry.Tu.GetCursor(loc);
        if (cursor.IsNull) return result;

        var canonical = cursor.CanonicalCursor;
        result.Spelling = canonical.Spelling.CString.ToString();
        result.Kind = canonical.Kind.ToString();
        result.DisplayName = canonical.DisplayName.CString.ToString();
        result.Type = canonical.Type.Spelling.CString.ToString();
        result.DeclarationLocation = ToLocation(canonical.Location);
        result.BriefComment = canonical.BriefCommentText.CString.ToString();
        return result;
    }

    public CppLocationResult GotoDefinition(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken ct)
    {
        var result = new CppLocationResult();
        if (!File.Exists(file)) return result;

        using var entry = AcquireTu(file, extraIncludes, extraDefines);
        if (entry.Tu.Handle == IntPtr.Zero) return result;

        var clangFile = entry.Tu.GetFile(file);
        var loc = entry.Tu.GetLocation(clangFile, (uint)line, (uint)column);
        var cursor = entry.Tu.GetCursor(loc);
        if (cursor.IsNull) return result;

        var def = cursor.Definition;
        if (def.IsNull) def = cursor.CanonicalCursor;

        result.Location = ToLocation(def.Location);
        result.Spelling = def.Spelling.CString.ToString();
        result.Kind = def.Kind.ToString();
        return result;
    }

    public CppLocationListResult FindReferencesInFiles(string seedFile, int line, int column, string[] additionalFiles, string[]? extraIncludes, string[]? extraDefines, CancellationToken ct)
    {
        var result = new CppLocationListResult();
        if (!File.Exists(seedFile)) return result;

        // Resolve USR from the seed file.
        string? rootUsr;
        string? rootSpelling;
        string? rootKind;
        using (var seedLease = AcquireTu(seedFile, extraIncludes, extraDefines))
        {
            if (seedLease.Tu.Handle == IntPtr.Zero) return result;
            var clangFile = seedLease.Tu.GetFile(seedFile);
            var loc = seedLease.Tu.GetLocation(clangFile, (uint)line, (uint)column);
            var cursor = seedLease.Tu.GetCursor(loc);
            var canonical = cursor.CanonicalCursor;
            if (canonical.IsNull) return result;
            rootUsr = canonical.Usr.CString.ToString();
            rootSpelling = canonical.Spelling.CString.ToString();
            rootKind = canonical.Kind.ToString();
        }
        if (string.IsNullOrEmpty(rootUsr)) return result;

        result.Spelling = rootSpelling;
        result.Kind = rootKind;

        // Always walk the seed file too.
        var seen = new HashSet<string>();
        var allFiles = new List<string> { seedFile };
        if (additionalFiles is not null) allFiles.AddRange(additionalFiles);

        foreach (var f in allFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(f)) continue;
            try
            {
                using var lease = AcquireTu(f, extraIncludes, extraDefines);
                if (lease.Tu.Handle == IntPtr.Zero) continue;
                var matches = WalkForReferences(lease.Tu, rootUsr!);
                foreach (var m in matches)
                {
                    var key = $"{m.File}|{m.Line}|{m.Column}";
                    if (seen.Add(key)) result.Locations.Add(m);
                }
            }
            catch
            {
                // Skip TUs that fail to parse; report partial results.
            }
        }

        result.Total = result.Locations.Count;
        return result;
    }

    public void Invalidate(string file)
    {
        var full = Path.GetFullPath(file);
        lock (_lock)
        {
            if (_tus.TryGetValue(full, out var node))
            {
                node.Value.Tu.Dispose();
                _lru.Remove(node);
                _tus.Remove(full);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var node in _tus.Values) node.Value.Tu.Dispose();
            _tus.Clear();
            _lru.Clear();
        }
        _index.Dispose();
    }

    [ThreadStatic]
    private static VisitState? s_visitState;

    private sealed class VisitState
    {
        public string Usr = "";
        public List<CppLocation> Matches = new();
    }

    private static unsafe List<CppLocation> WalkForReferences(CXTranslationUnit tu, string usr)
    {
        var state = new VisitState { Usr = usr };
        s_visitState = state;
        try
        {
            tu.Cursor.VisitChildren(&VisitForReferences, default);
        }
        finally
        {
            s_visitState = null;
        }
        return state.Matches;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe CXChildVisitResult VisitForReferences(CXCursor cursor, CXCursor parent, void* _)
    {
        var state = s_visitState;
        if (state is null) return CXChildVisitResult.CXChildVisit_Continue;
        try
        {
            var refCursor = cursor.Referenced;
            if (!refCursor.IsNull)
            {
                var refUsr = refCursor.CanonicalCursor.Usr.CString.ToString();
                if (!string.IsNullOrEmpty(refUsr) && refUsr == state.Usr)
                {
                    var l = ToLocation(cursor.Location);
                    if (l is not null) state.Matches.Add(l);
                }
            }
        }
        catch { /* swallow per-cursor failures */ }
        return CXChildVisitResult.CXChildVisit_Recurse;
    }

    private static CppLocation? ToLocation(CXSourceLocation loc)
    {
        loc.GetSpellingLocation(out var f, out var line, out var col, out _);
        var fileName = f.Name.CString.ToString();
        if (string.IsNullOrEmpty(fileName)) return null;
        return new CppLocation
        {
            File = fileName,
            Line = (int)line,
            Column = (int)col,
        };
    }

    private TuLease AcquireTu(string file, string[]? extraIncludes, string[]? extraDefines)
    {
        var full = Path.GetFullPath(file);
        lock (_lock)
        {
            if (_tus.TryGetValue(full, out var node))
            {
                // Cache hit — bump to MRU and return.
                _lru.Remove(node);
                _lru.AddFirst(node);
                return new TuLease(node.Value);
            }
        }

        var args = BuildClangArgs(full, extraIncludes, extraDefines);
        var unit = CXTranslationUnit.Parse(_index, full, args, ReadOnlySpan<CXUnsavedFile>.Empty,
            CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord
            | CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing
            | CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies);

        var cached = new CachedTu(full, unit);
        lock (_lock)
        {
            // Race: another caller may have populated the slot.
            if (_tus.TryGetValue(full, out var existing))
            {
                cached.Tu.Dispose();
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return new TuLease(existing.Value);
            }
            var node = _lru.AddFirst(cached);
            _tus[full] = node;
            EvictIfNeeded();
        }
        return new TuLease(cached);
    }

    private void EvictIfNeeded()
    {
        while (_tus.Count > MaxCachedTus)
        {
            var lru = _lru.Last;
            if (lru is null) break;
            lru.Value.Tu.Dispose();
            _tus.Remove(lru.Value.FilePath);
            _lru.RemoveLast();
        }
    }

    private static string[] BuildClangArgs(string file, string[]? extraIncludes, string[]? extraDefines)
    {
        var args = new List<string>
        {
            "-xc++",
            "-std=c++20",
            "-fparse-all-comments",
        };

        // System includes will be added in F-2 (IncludeResolver).
        if (extraIncludes is not null)
            foreach (var inc in extraIncludes)
                if (!string.IsNullOrWhiteSpace(inc)) args.Add($"-I{inc}");

        if (extraDefines is not null)
            foreach (var d in extraDefines)
                if (!string.IsNullOrWhiteSpace(d)) args.Add($"-D{d}");

        return args.ToArray();
    }

    private sealed class CachedTu
    {
        public string FilePath { get; }
        public CXTranslationUnit Tu { get; }
        public CachedTu(string filePath, CXTranslationUnit tu) { FilePath = filePath; Tu = tu; }
    }

    private readonly struct TuLease : IDisposable
    {
        public CXTranslationUnit Tu { get; }
        public TuLease(CachedTu cached) { Tu = cached.Tu; }
        public void Dispose() { /* TU stays cached */ }
    }
}
