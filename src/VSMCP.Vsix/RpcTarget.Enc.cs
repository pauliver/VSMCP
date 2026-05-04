using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M20: Edit & Continue (issue #71) --------
    //
    // First cut: delegates to VS's existing ENC/Hot Reload pipeline via DTE commands.
    // A deeper integration would use IDebugEncNotify + IVsENCRebuildableProjectCfg2 to
    // inspect rude edits and language-specific diagnostics — left for a follow-up.

    public async Task<EncStatusResult> DebugEncStatusAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        var result = new EncStatusResult();
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
        {
            result.Reason = "DTE service unavailable.";
            return result;
        }

        var mode = dte.Debugger?.CurrentMode ?? EnvDTE.dbgDebugMode.dbgDesignMode;
        result.DebuggingActive = mode != EnvDTE.dbgDebugMode.dbgDesignMode;
        result.InBreakMode = mode == EnvDTE.dbgDebugMode.dbgBreakMode;

        if (!result.DebuggingActive)
        {
            result.Available = false;
            result.Reason = "Not in a debug session. Start debugging first; ENC applies on Continue from a paused state, or via Hot Reload while running.";
            return result;
        }

        // Check whether the Debug.ApplyCodeChanges command is currently dispatchable.
        // DTE.Commands.Item lets us look it up; .IsAvailable reflects whether the menu would be enabled.
        try
        {
            var cmd = dte.Commands.Item("Debug.ApplyCodeChanges", -1);
            if (cmd is null)
            {
                result.Available = false;
                result.Reason = "Debug.ApplyCodeChanges command not registered (older VS or missing language pack).";
            }
            else
            {
                result.Available = cmd.IsAvailable;
                if (!result.Available)
                    result.Reason = result.InBreakMode
                        ? "ENC is not applicable for the current debug engine or project (e.g. release-optimized, native attach without symbols, or a runtime that doesn't support ENC)."
                        : "Debugger is running. Pause first (Debug.BreakAll) and edit; then call this tool.";
            }
        }
        catch (Exception ex)
        {
            result.Available = false;
            result.Reason = $"Could not query Debug.ApplyCodeChanges availability: {ex.Message}";
        }
        return result;
    }

    public async Task<EncApplyResult> DebugApplyCodeChangesAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return new EncApplyResult { Success = false, Message = "DTE service unavailable." };

        var mode = dte.Debugger?.CurrentMode ?? EnvDTE.dbgDebugMode.dbgDesignMode;
        if (mode == EnvDTE.dbgDebugMode.dbgDesignMode)
            return new EncApplyResult { Success = false, Message = "Not in a debug session — start debugging first." };

        // Save all dirty buffers so VS sees the latest edits before applying.
        try { dte.Documents?.SaveAll(); } catch { }

        try
        {
            dte.ExecuteCommand("Debug.ApplyCodeChanges");
            return new EncApplyResult
            {
                Success = true,
                Message = mode == EnvDTE.dbgDebugMode.dbgBreakMode
                    ? "Apply requested. Continue execution to see the new code take effect."
                    : "Apply requested while running (Hot Reload path).",
            };
        }
        catch (Exception ex)
        {
            return new EncApplyResult
            {
                Success = false,
                Message = $"Debug.ApplyCodeChanges failed: {ex.Message}. " +
                          "Common causes: rude edit (e.g. changing a method's signature), runtime doesn't support ENC, or build configuration prevents it.",
            };
        }
    }

    public async Task<EncApplyResult> DebugHotReloadAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return new EncApplyResult { Success = false, Message = "DTE service unavailable." };

        try { dte.Documents?.SaveAll(); } catch { }

        // VS 17.4+ added Debug.HotReloadApplyCodeChanges; older builds use the same Debug.ApplyCodeChanges.
        // Try the Hot Reload-specific command first, fall back.
        try
        {
            dte.ExecuteCommand("Debug.HotReloadApplyCodeChanges");
            return new EncApplyResult { Success = true, Message = "Hot Reload applied." };
        }
        catch
        {
            try
            {
                dte.ExecuteCommand("Debug.ApplyCodeChanges");
                return new EncApplyResult { Success = true, Message = "Hot Reload command not available; fell back to Debug.ApplyCodeChanges." };
            }
            catch (Exception ex)
            {
                return new EncApplyResult { Success = false, Message = $"Both Hot Reload variants failed: {ex.Message}" };
            }
        }
    }
}
