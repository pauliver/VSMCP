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
    // Dirty editor buffers: file path → UTF-8 bytes of in-memory content. When set, libclang
    // sees this content rather than the on-disk file (CXUnsavedFile). Cleared by
    // SetUnsavedBuffer(file, null) once the editor saves.
    private readonly Dictionary<string, byte[]> _unsavedBuffers = new(StringComparer.OrdinalIgnoreCase);
    // PCH header per source file: file → header path (e.g. "C:\proj\pch.h"). When set, the
    // header is compiled to a clang .pch and -include-pch is added to the source's parse args.
    private readonly Dictionary<string, string> _pchByFile = new(StringComparer.OrdinalIgnoreCase);
    // Compiled-PCH cache: header path → compiled .pch path. Populated lazily by EnsureCompiledPch.
    private readonly Dictionary<string, string?> _compiledPch = new(StringComparer.OrdinalIgnoreCase);
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
            DropAllForFileLocked(full);
        }
    }

    /// <summary>
    /// Set or clear the unsaved-buffer override for a single file. Pass null/empty content
    /// to clear. Drops the cached TU for this file so the next acquire reparses with the
    /// new content.
    /// </summary>
    public void SetUnsavedBuffer(string file, string? content)
    {
        var full = Path.GetFullPath(file);
        lock (_lock)
        {
            if (string.IsNullOrEmpty(content))
            {
                _unsavedBuffers.Remove(full);
            }
            else
            {
                // libclang wants raw bytes (UTF-8 in practice for C++ source).
                _unsavedBuffers[full] = System.Text.Encoding.UTF8.GetBytes(content!);
            }
            // Also drop any cached TU (all args variants) for this file so the next call
            // reparses with the override visible.
            DropAllForFileLocked(full);
        }
    }

    public void SetPchHeader(string file, string? pchHeader)
    {
        var full = Path.GetFullPath(file);
        lock (_lock)
        {
            if (string.IsNullOrEmpty(pchHeader)) _pchByFile.Remove(full);
            else _pchByFile[full] = Path.GetFullPath(pchHeader!);
            DropAllForFileLocked(full);
        }
    }

    /// <summary>
    /// Ensure the clang-format precompiled header for <paramref name="headerPath"/> exists on
    /// disk and return its path. First call compiles + caches; subsequent calls return the
    /// cached path. Returns null if compilation failed.
    /// </summary>
    private string? EnsureCompiledPch(string headerPath, string[] baseArgs)
    {
        var fullHeader = Path.GetFullPath(headerPath);
        lock (_lock)
        {
            if (_compiledPch.TryGetValue(fullHeader, out var existing)) return existing;
        }

        try
        {
            // Cache key: the header's full path + last-write-time. If the header changes,
            // the filename also changes so we naturally rebuild.
            var stamp = File.Exists(fullHeader) ? File.GetLastWriteTimeUtc(fullHeader).Ticks : 0L;
            var rawKey = fullHeader.ToLowerInvariant() + "|" + stamp;
            var hashBytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            var pchDir = Path.Combine(Path.GetTempPath(), "vsmcp-pch");
            Directory.CreateDirectory(pchDir);
            var pchPath = Path.Combine(pchDir, hash + ".pch");

            if (File.Exists(pchPath))
            {
                lock (_lock) _compiledPch[fullHeader] = pchPath;
                return pchPath;
            }

            // PCH-build args: same -I/-D as the source's parse, but -xc++-header instead.
            var pchArgs = new List<string> { "-xc++-header", "-std=c++20" };
            foreach (var a in baseArgs)
            {
                if (a == "-xc++" || a == "-std=c++20" || a == "-fparse-all-comments") continue;
                pchArgs.Add(a);
            }

            var pchTu = CXTranslationUnit.Parse(_index, fullHeader, pchArgs.ToArray(), ReadOnlySpan<CXUnsavedFile>.Empty,
                CXTranslationUnit_Flags.CXTranslationUnit_ForSerialization
                | CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing);
            if (pchTu.Handle == IntPtr.Zero)
            {
                lock (_lock) _compiledPch[fullHeader] = null;
                return null;
            }
            try
            {
                var rc = pchTu.Save(pchPath, CXSaveTranslationUnit_Flags.CXSaveTranslationUnit_None);
                if (rc != CXSaveError.CXSaveError_None)
                {
                    lock (_lock) _compiledPch[fullHeader] = null;
                    return null;
                }
                lock (_lock) _compiledPch[fullHeader] = pchPath;
                return pchPath;
            }
            finally
            {
                pchTu.Dispose();
            }
        }
        catch
        {
            lock (_lock) _compiledPch[fullHeader] = null;
            return null;
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

    private unsafe TuLease AcquireTu(string file, string[]? extraIncludes, string[]? extraDefines)
    {
        var full = Path.GetFullPath(file);

        var args = BuildClangArgs(full, extraIncludes, extraDefines);

        // PCH? If the file has a registered precompiled-header source, compile it
        // (cached) and prepend -include-pch to the args.
        string? pchHeader;
        lock (_lock) { _pchByFile.TryGetValue(full, out pchHeader); }
        if (!string.IsNullOrEmpty(pchHeader))
        {
            var compiledPch = EnsureCompiledPch(pchHeader!, args);
            if (!string.IsNullOrEmpty(compiledPch))
            {
                var augmented = new List<string>(args.Length + 2) { "-include-pch", compiledPch! };
                augmented.AddRange(args);
                args = augmented.ToArray();
            }
        }

        // Cache key = file path + effective clang args, so callers with different includes/
        // defines coexist as separate entries (one path-keyed slot made them dispose each
        // other's TU on every alternation — a full reparse per call). Validity within an entry
        // is the on-disk timestamp. (Unsaved-buffer changes invalidate via cpp_invalidate.)
        var argsKey = string.Join("\u0001", args);
        var cacheKey = full + "\u0001" + argsKey;
        DateTime diskStamp;
        try { diskStamp = File.GetLastWriteTimeUtc(full); } catch { diskStamp = DateTime.MinValue; }

        lock (_lock)
        {
            if (_tus.TryGetValue(cacheKey, out var node))
            {
                if (node.Value.DiskStamp == diskStamp)
                {
                    // Cache hit — bump to MRU and return.
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    node.Value.Leases++;
                    return new TuLease(this, node.Value);
                }

                // Stale (file changed on disk) — drop and reparse below.
                RemoveAndReleaseLocked(node);
            }
        }

        // Snapshot the unsaved buffers. Pin the bytes so libclang can read them directly.
        KeyValuePair<string, byte[]>[] snapshot;
        lock (_lock) { snapshot = _unsavedBuffers.ToArray(); }

        var pinnedFilenames = new List<GCHandle>(snapshot.Length);
        var pinnedContents = new List<GCHandle>(snapshot.Length);
        var unsavedArr = snapshot.Length == 0 ? null : new CXUnsavedFile[snapshot.Length];
        try
        {
            for (int i = 0; i < snapshot.Length; i++)
            {
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(snapshot[i].Key + "\0");
                var contentBytes = snapshot[i].Value;
                var nameHandle = GCHandle.Alloc(nameBytes, GCHandleType.Pinned);
                var contentHandle = GCHandle.Alloc(contentBytes, GCHandleType.Pinned);
                pinnedFilenames.Add(nameHandle);
                pinnedContents.Add(contentHandle);
                unsavedArr![i] = new CXUnsavedFile
                {
                    Filename = (sbyte*)nameHandle.AddrOfPinnedObject(),
                    Contents = (sbyte*)contentHandle.AddrOfPinnedObject(),
                    Length = (UIntPtr)contentBytes.Length,
                };
            }

            var unsavedSpan = unsavedArr is null ? ReadOnlySpan<CXUnsavedFile>.Empty : unsavedArr.AsSpan();
            // Note: SkipFunctionBodies is intentionally NOT set here. Diagnostic errors and
            // call-site references live INSIDE function bodies; skipping bodies makes the
            // analyzer return false-clean diagnostics for syntax errors in dirty buffers
            // (was bug #116). The perf cost is real but correctness wins.
            var unit = CXTranslationUnit.Parse(_index, full, args, unsavedSpan,
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord
                | CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing);

            var cached = new CachedTu(full, cacheKey, unit, diskStamp);
            lock (_lock)
            {
                // Race: another caller may have populated the slot.
                if (_tus.TryGetValue(cacheKey, out var existing))
                {
                    if (existing.Value.DiskStamp == diskStamp)
                    {
                        cached.Tu.Dispose(); // fresh duplicate, never exposed — safe to dispose inline
                        _lru.Remove(existing);
                        _lru.AddFirst(existing);
                        existing.Value.Leases++;
                        return new TuLease(this, existing.Value);
                    }
                    RemoveAndReleaseLocked(existing);
                }
                var node = _lru.AddFirst(cached);
                _tus[cacheKey] = node;
                cached.Leases++;
                EvictIfNeeded();
            }
            return new TuLease(this, cached);
        }
        finally
        {
            foreach (var h in pinnedFilenames) if (h.IsAllocated) h.Free();
            foreach (var h in pinnedContents) if (h.IsAllocated) h.Free();
        }
    }

    private void EvictIfNeeded()
    {
        while (_tus.Count > MaxCachedTus)
        {
            var lru = _lru.Last;
            if (lru is null) break;
            RemoveAndReleaseLocked(lru);
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
        /// <summary>Dictionary key: file path + effective args — different args coexist as
        /// separate entries instead of disposing each other's TU on every alternation.</summary>
        public string CacheKey { get; }
        public CXTranslationUnit Tu { get; }
        /// <summary>On-disk LastWriteTimeUtc at parse time — a newer file means this TU is stale.</summary>
        public DateTime DiskStamp { get; }

        // Guarded by the outer _lock. A TU with outstanding leases is never disposed inline —
        // disposal is deferred to the last lease's Dispose (native use-after-free otherwise).
        public int Leases;
        public bool PendingDispose;

        public CachedTu(string filePath, string cacheKey, CXTranslationUnit tu, DateTime diskStamp)
        {
            FilePath = filePath;
            CacheKey = cacheKey;
            Tu = tu;
            DiskStamp = diskStamp;
        }
    }

    /// <summary>Remove an entry from the cache and dispose its TU — deferred while leases are
    /// outstanding. Caller must hold <see cref="_lock"/>.</summary>
    private void RemoveAndReleaseLocked(LinkedListNode<CachedTu> node)
    {
        _lru.Remove(node);
        _tus.Remove(node.Value.CacheKey);
        if (node.Value.Leases == 0) node.Value.Tu.Dispose();
        else node.Value.PendingDispose = true;
    }

    /// <summary>Drop every cached entry (any args variant) for a file. Caller holds _lock.</summary>
    private void DropAllForFileLocked(string fullPath)
    {
        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            if (string.Equals(node.Value.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                RemoveAndReleaseLocked(node);
            node = next;
        }
    }

    private readonly struct TuLease : IDisposable
    {
        private readonly CppAnalysis _owner;
        private readonly CachedTu _cached;
        public CXTranslationUnit Tu => _cached.Tu;

        /// <summary>Caller must already have incremented <c>cached.Leases</c> under the lock.</summary>
        public TuLease(CppAnalysis owner, CachedTu cached)
        {
            _owner = owner;
            _cached = cached;
        }

        public void Dispose()
        {
            lock (_owner._lock)
            {
                _cached.Leases--;
                if (_cached.PendingDispose && _cached.Leases == 0)
                    _cached.Tu.Dispose();
            }
        }
    }
}
