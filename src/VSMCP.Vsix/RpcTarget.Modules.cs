using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<ModuleListResult> ModulesListAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var tracker = _package.Modules
            ?? throw new VsmcpException(ErrorCodes.WrongState, "Module tracking is not initialized.");

        var result = new ModuleListResult();
        foreach (var m in tracker.Snapshot())
            result.Modules.Add(m);
        return result;
    }

    public async Task<SymbolStatusResult> SymbolsLoadAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new VsmcpException(ErrorCodes.NotFound, "moduleId is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var tracker = _package.Modules
            ?? throw new VsmcpException(ErrorCodes.WrongState, "Module tracking is not initialized.");

        if (!tracker.TryGet(moduleId, out var module) || module is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"No module with id '{moduleId}'.");

        if (module is IDebugModule3 m3)
        {
            try
            {
                var hr = m3.LoadSymbols();
                if (hr != VSConstants.S_OK && hr != 1 /* S_FALSE: already loaded / not applicable */)
                    throw new VsmcpException(ErrorCodes.InteropFault, $"LoadSymbols returned HRESULT 0x{hr:X8}.");
            }
            catch (VsmcpException) { throw; }
            catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"LoadSymbols failed: {ex.Message}", ex); }
        }
        else
        {
            throw new VsmcpException(ErrorCodes.Unsupported, "This module's engine does not expose IDebugModule3; cannot force symbol load.");
        }

        return ReadStatus(moduleId, module);
    }

    public async Task<SymbolStatusResult> SymbolsStatusAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new VsmcpException(ErrorCodes.NotFound, "moduleId is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var tracker = _package.Modules
            ?? throw new VsmcpException(ErrorCodes.WrongState, "Module tracking is not initialized.");

        if (!tracker.TryGet(moduleId, out var module) || module is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"No module with id '{moduleId}'.");

        return ReadStatus(moduleId, module);
    }

    private static SymbolStatusResult ReadStatus(string moduleId, IDebugModule2 module)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new SymbolStatusResult { ModuleId = moduleId, State = SymbolState.Unknown };

        // Prefer IDebugModule3 which can tell us "loaded / not loaded / stripped" plus a status message.
        if (module is IDebugModule3 m3)
        {
            var arr = new MODULE_SYMBOL_SEARCH_INFO[1];
            try
            {
                if (m3.GetSymbolInfo(enum_SYMBOL_SEARCH_INFO_FIELDS.SSIF_VERBOSE_SEARCH_INFO, arr) == VSConstants.S_OK)
                {
                    result.Message = arr[0].bstrVerboseSearchInfo;
                }
            }
            catch { }
        }

        // Flags from MODULE_INFO are the authoritative load-state source.
        var infoArr = new MODULE_INFO[1];
        try
        {
            if (module.GetInfo(
                enum_MODULE_INFO_FIELDS.MIF_FLAGS
                | enum_MODULE_INFO_FIELDS.MIF_URLSYMBOLLOCATION, infoArr) == VSConstants.S_OK)
            {
                var mi = infoArr[0];
                if ((mi.dwValidFields & enum_MODULE_INFO_FIELDS.MIF_FLAGS) != 0)
                {
                    result.State = (mi.m_dwModuleFlags & enum_MODULE_FLAGS.MODULE_FLAG_SYMBOLS) != 0
                        ? SymbolState.Loaded
                        : SymbolState.NotLoaded;
                }
                if ((mi.dwValidFields & enum_MODULE_INFO_FIELDS.MIF_URLSYMBOLLOCATION) != 0)
                    result.SymbolPath = mi.m_bstrUrlSymbolLocation;
            }
        }
        catch { }

        return result;
    }
}
