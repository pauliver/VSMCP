using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Subscribes to IVsDebugger debug events and caches loaded modules (IDebugModule2) per program.
/// We hold the native objects so that LoadSymbols / GetSymbolInfo still work after load.
/// </summary>
internal sealed class ModuleTracker : IDebugEventCallback2, IDisposable
{
    private static readonly Guid s_moduleLoadEvent = typeof(IDebugModuleLoadEvent2).GUID;
    private static readonly Guid s_programDestroyEvent = typeof(IDebugProgramDestroyEvent2).GUID;
    private static readonly Guid s_programCreateEvent = typeof(IDebugProgramCreateEvent2).GUID;

    private readonly IVsDebugger? _debugger;
    private readonly ConcurrentDictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    // Raw module -> id so we can remove on unload.
    private readonly ConcurrentDictionary<IDebugModule2, string> _byModule = new();
    private IDebugProgram2? _currentProgram;
    private IDebugThread2? _lastThread;
    private bool _advised;

    public IDebugProgram2? CurrentProgram => _currentProgram;
    public IDebugThread2? LastThread => _lastThread;

    public ModuleTracker(IVsDebugger? debugger)
    {
        _debugger = debugger;
        if (_debugger is not null)
        {
            try
            {
                _debugger.AdviseDebugEventCallback(this);
                _advised = true;
            }
            catch { }
        }
    }

    public IReadOnlyList<ModuleInfo> Snapshot()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var list = new List<ModuleInfo>();
        foreach (var entry in _byId.Values)
        {
            var refreshed = ReadInfo(entry.Module);
            refreshed.Id = entry.Info.Id;
            list.Add(refreshed);
        }
        list.Sort((a, b) =>
        {
            var ao = a.Order ?? int.MaxValue;
            var bo = b.Order ?? int.MaxValue;
            if (ao != bo) return ao.CompareTo(bo);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    public bool TryGet(string id, out IDebugModule2? module)
    {
        if (_byId.TryGetValue(id, out var entry))
        {
            module = entry.Module;
            return true;
        }
        module = null;
        return false;
    }

    int IDebugEventCallback2.Event(
        IDebugEngine2 pEngine,
        IDebugProcess2 pProcess,
        IDebugProgram2 pProgram,
        IDebugThread2 pThread,
        IDebugEvent2 pEvent,
        ref Guid riidEvent,
        uint dwAttrib)
    {
        try
        {
            // Always track the latest program/thread we've seen so inspection RPCs have a target.
            if (pProgram is not null) _currentProgram = pProgram;
            if (pThread is not null) _lastThread = pThread;

            if (riidEvent == s_moduleLoadEvent && pEvent is IDebugModuleLoadEvent2 modLoad)
            {
                string? dbgMsg = null;
                int fLoad = 0;
                modLoad.GetModule(out var module, ref dbgMsg, ref fLoad);
                if (module is not null)
                {
                    if (fLoad != 0) AddOrUpdate(module);
                    else Remove(module);
                }
            }
            else if (riidEvent == s_programCreateEvent && pProgram is not null)
            {
                _currentProgram = pProgram;
            }
            else if (riidEvent == s_programDestroyEvent)
            {
                ClearAll();
                _currentProgram = null;
                _lastThread = null;
            }
        }
        catch { /* never let a callback exception escape into the debugger */ }

        return VSConstants.S_OK;
    }

    private void AddOrUpdate(IDebugModule2 module)
    {
        var info = ReadInfo(module);
        if (_byModule.TryGetValue(module, out var existingId))
        {
            info.Id = existingId;
            _byId[existingId] = new Entry(module, info);
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        info.Id = id;
        _byModule[module] = id;
        _byId[id] = new Entry(module, info);
    }

    private void Remove(IDebugModule2 module)
    {
        if (_byModule.TryRemove(module, out var id))
            _byId.TryRemove(id, out _);
    }

    private void ClearAll()
    {
        _byId.Clear();
        _byModule.Clear();
    }

    private static ModuleInfo ReadInfo(IDebugModule2 module)
    {
        var info = new ModuleInfo();
        var arr = new MODULE_INFO[1];
        try
        {
            if (module.GetInfo(
                enum_MODULE_INFO_FIELDS.MIF_NAME
                | enum_MODULE_INFO_FIELDS.MIF_URL
                | enum_MODULE_INFO_FIELDS.MIF_LOADADDRESS
                | enum_MODULE_INFO_FIELDS.MIF_LOADORDER
                | enum_MODULE_INFO_FIELDS.MIF_SIZE
                | enum_MODULE_INFO_FIELDS.MIF_VERSION
                | enum_MODULE_INFO_FIELDS.MIF_URLSYMBOLLOCATION
                | enum_MODULE_INFO_FIELDS.MIF_FLAGS, arr) == VSConstants.S_OK)
            {
                var mi = arr[0];
                var v = mi.dwValidFields;
                if ((v & enum_MODULE_INFO_FIELDS.MIF_NAME) != 0) info.Name = mi.m_bstrName ?? "";
                if ((v & enum_MODULE_INFO_FIELDS.MIF_URL) != 0) info.Path = mi.m_bstrUrl;
                if ((v & enum_MODULE_INFO_FIELDS.MIF_LOADADDRESS) != 0) info.LoadAddress = "0x" + mi.m_addrLoadAddress.ToString("X");
                if ((v & enum_MODULE_INFO_FIELDS.MIF_LOADORDER) != 0) info.Order = (int)mi.m_dwLoadOrder;
                if ((v & enum_MODULE_INFO_FIELDS.MIF_SIZE) != 0) info.Size = mi.m_dwSize;
                if ((v & enum_MODULE_INFO_FIELDS.MIF_VERSION) != 0) info.Version = mi.m_bstrVersion;
                if ((v & enum_MODULE_INFO_FIELDS.MIF_URLSYMBOLLOCATION) != 0) info.SymbolPath = mi.m_bstrUrlSymbolLocation;

                if ((v & enum_MODULE_INFO_FIELDS.MIF_FLAGS) != 0)
                {
                    var flags = mi.m_dwModuleFlags;
                    info.Is64Bit = (flags & enum_MODULE_FLAGS.MODULE_FLAG_64BIT) != 0;
                    info.IsUserCode = (flags & enum_MODULE_FLAGS.MODULE_FLAG_SYSTEM) == 0;
                    info.SymbolState = (flags & enum_MODULE_FLAGS.MODULE_FLAG_SYMBOLS) != 0
                        ? SymbolState.Loaded
                        : SymbolState.NotLoaded;
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(info.Name) && !string.IsNullOrEmpty(info.Path))
        {
            try { info.Name = System.IO.Path.GetFileName(info.Path); } catch { }
        }

        return info;
    }

    public void Dispose()
    {
        if (_advised && _debugger is not null)
        {
            try { _debugger.UnadviseDebugEventCallback(this); } catch { }
        }
        _advised = false;
        ClearAll();
    }

    private readonly struct Entry
    {
        public Entry(IDebugModule2 module, ModuleInfo info) { Module = module; Info = info; }
        public IDebugModule2 Module { get; }
        public ModuleInfo Info { get; }
    }
}
