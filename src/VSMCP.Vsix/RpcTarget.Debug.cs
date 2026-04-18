using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<DebugActionResult> DebugLaunchAsync(DebugLaunchOptions options, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        var sb = dte.Solution?.SolutionBuild
            ?? throw new VsmcpException(ErrorCodes.WrongState, "No solution is open.");

        string? previousStartup = null;
        if (!string.IsNullOrWhiteSpace(options.ProjectId))
        {
            try
            {
                if (sb.StartupProjects is Array arr && arr.Length > 0 && arr.GetValue(0) is string sp)
                    previousStartup = sp;
            }
            catch { }

            var project = VsHelpers.RequireProject(dte.Solution, options.ProjectId!);
            try { sb.StartupProjects = project.UniqueName; } catch { }

            if (options.Args is not null || options.Cwd is not null || options.Env is not null)
                ApplyLaunchProperties(project, options);
        }

        try
        {
            if (options.NoDebug)
                sb.Run();
            else
                dte.Debugger.Go(WaitForBreakOrEnd: false);
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to launch: {ex.Message}", ex);
        }

        return Result("Debug session started.", options.ProjectId is null ? null : $"startup={options.ProjectId}");
    }

    public async Task<DebugActionResult> DebugAttachAsync(DebugAttachOptions options, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        if (options.Pid is null && string.IsNullOrWhiteSpace(options.ProcessName))
            throw new VsmcpException(ErrorCodes.NotFound, "Either Pid or ProcessName must be supplied.");

        var debugger = dte.Debugger as EnvDTE80.Debugger2
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "Debugger2 interface unavailable.");

        EnvDTE80.Process2? target = null;
        foreach (EnvDTE.Process p in debugger.LocalProcesses)
        {
            if (p is not EnvDTE80.Process2 p2) continue;
            if (options.Pid is int pid && p2.ProcessID == pid) { target = p2; break; }
            if (!string.IsNullOrWhiteSpace(options.ProcessName)
                && string.Equals(Path.GetFileNameWithoutExtension(p2.Name), options.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                target = p2;
                break;
            }
        }

        if (target is null)
            throw new VsmcpException(ErrorCodes.NotFound,
                options.Pid is int pid ? $"No local process with pid {pid}." : $"No local process named '{options.ProcessName}'.");

        try
        {
            if (options.Engines is { Count: > 0 })
            {
                var engines = options.Engines.ToArray();
                target.Attach2(engines);
            }
            else
            {
                target.Attach();
            }
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.InteropFault, $"Attach failed: {ex.Message}", ex);
        }

        return Result("Attached.", $"pid={target.ProcessID}, name={target.Name}");
    }

    public async Task<DebugActionResult> DebugStopAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.Stop(WaitForDesignMode: false); } catch (Exception ex) { throw Interop("stop", ex); }
        return Result("Stopped.");
    }

    public async Task<DebugActionResult> DebugDetachAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.DetachAll(); } catch (Exception ex) { throw Interop("detach", ex); }
        return Result("Detached.");
    }

    public async Task<DebugActionResult> DebugRestartAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try
        {
            if (dte.Debugger is EnvDTE80.Debugger2 dbg2)
                dte.ExecuteCommand("Debug.Restart");
            else
                dte.Debugger.Stop(WaitForDesignMode: true);
        }
        catch (Exception ex) { throw Interop("restart", ex); }
        return Result("Restart requested.");
    }

    public async Task<DebugActionResult> DebugBreakAllAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.Break(WaitForBreakMode: false); } catch (Exception ex) { throw Interop("break", ex); }
        return Result("Break requested.");
    }

    public async Task<DebugActionResult> DebugContinueAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.Go(WaitForBreakOrEnd: false); } catch (Exception ex) { throw Interop("continue", ex); }
        return Result("Continued.");
    }

    public async Task<DebugActionResult> DebugStepIntoAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.StepInto(WaitForBreakOrEnd: false); } catch (Exception ex) { throw Interop("step into", ex); }
        return Result("Step into.");
    }

    public async Task<DebugActionResult> DebugStepOverAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.StepOver(WaitForBreakOrEnd: false); } catch (Exception ex) { throw Interop("step over", ex); }
        return Result("Step over.");
    }

    public async Task<DebugActionResult> DebugStepOutAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        try { dte.Debugger.StepOut(WaitForBreakOrEnd: false); } catch (Exception ex) { throw Interop("step out", ex); }
        return Result("Step out.");
    }

    public async Task<DebugActionResult> DebugRunToCursorAsync(string file, int line, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "File is required.");
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        MoveCaret(dte, file, line, 1);
        try { dte.ExecuteCommand("Debug.RunToCursor"); }
        catch (Exception ex) { throw Interop("run to cursor", ex); }
        return Result("Run to cursor.", $"{file}:{line}");
    }

    public async Task<DebugActionResult> DebugSetNextStatementAsync(string file, int line, bool allowSideEffects, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        if (!allowSideEffects)
            throw new VsmcpException(ErrorCodes.WrongState,
                "Set-next-statement can skip constructors, leak resources, or corrupt program state. Pass allowSideEffects=true to proceed.");
        if (string.IsNullOrWhiteSpace(file)) throw new VsmcpException(ErrorCodes.NotFound, "File is required.");
        if (!File.Exists(file)) throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {file}");

        if (dte.Debugger.CurrentMode != EnvDTE.dbgDebugMode.dbgBreakMode)
            throw new VsmcpException(ErrorCodes.WrongState, "Debugger must be in Break mode to set next statement.");

        MoveCaret(dte, file, line, 1);
        try { dte.ExecuteCommand("Debug.SetNextStatement"); }
        catch (Exception ex) { throw Interop("set next statement", ex); }
        return Result("Set next statement.", $"{file}:{line}");
    }

    public async Task<DebugInfo> DebugStateAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        return SnapshotDebugInfo(dte);
    }

    // -------- helpers --------

    private async Task<EnvDTE80.DTE2> RequireDteAsync()
    {
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
        return dte;
    }

    private static DebugActionResult Result(string note, string? detail = null)
    {
        var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
        return new DebugActionResult
        {
            Note = detail is null ? note : $"{note} ({detail})",
            Info = dte is null ? new DebugInfo() : SnapshotDebugInfo(dte),
        };
    }

    private static VsmcpException Interop(string action, Exception ex)
        => new(ErrorCodes.InteropFault, $"Failed to {action}: {ex.Message}", ex);

    private static DebugInfo SnapshotDebugInfo(EnvDTE80.DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var info = new DebugInfo { Mode = DebugMode.Design, StoppedReason = DebugStoppedReason.Unknown };

        var debugger = dte.Debugger;
        if (debugger is null) return info;

        info.Mode = debugger.CurrentMode switch
        {
            EnvDTE.dbgDebugMode.dbgBreakMode => DebugMode.Break,
            EnvDTE.dbgDebugMode.dbgRunMode => DebugMode.Run,
            _ => DebugMode.Design,
        };

        try
        {
            var currentProcess = debugger.CurrentProcess;
            if (currentProcess is not null)
            {
                info.CurrentProcessId = currentProcess.ProcessID;
                info.CurrentProcessName = currentProcess.Name;
            }
        }
        catch { }

        if (info.Mode == DebugMode.Break)
        {
            info.StoppedReason = DebugStoppedReason.UserBreak;
            try
            {
                var thread = debugger.CurrentThread;
                if (thread is not null)
                    info.CurrentThread = new DebugThreadInfo { Id = thread.ID, Name = thread.Name, IsCurrent = true };
            }
            catch { }

            try
            {
                var frame = debugger.CurrentStackFrame;
                if (frame is not null)
                {
                    var f = new DebugFrameInfo
                    {
                        Index = 0,
                        FunctionName = frame.FunctionName ?? "",
                        Language = frame.Language,
                    };

                    try
                    {
                        var doc = dte.ActiveDocument;
                        if (doc?.Selection is EnvDTE.TextSelection sel)
                        {
                            f.File = doc.FullName;
                            f.Line = sel.CurrentLine;
                            f.Column = sel.CurrentColumn;
                        }
                    }
                    catch { }

                    info.CurrentFrame = f;
                }
            }
            catch { }

            try
            {
                var ex = debugger.GetExpression("$exception", true, 200);
                if (ex is not null && ex.IsValidValue && !string.IsNullOrEmpty(ex.Value))
                {
                    info.StoppedReason = DebugStoppedReason.Exception;
                    info.LastExceptionMessage = ex.Value;
                }
            }
            catch { }
        }

        return info;
    }

    private static void MoveCaret(EnvDTE80.DTE2 dte, string file, int line, int column)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var window = dte.ItemOperations.OpenFile(file, EnvDTE.Constants.vsViewKindPrimary);
        if (window?.Document?.Selection is EnvDTE.TextSelection sel)
            sel.MoveToLineAndOffset(Math.Max(1, line), Math.Max(1, column));
    }

    private static void ApplyLaunchProperties(EnvDTE.Project project, DebugLaunchOptions options)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var cfg = project.ConfigurationManager?.ActiveConfiguration;
            var props = cfg?.Properties;
            if (props is null) return;

            TrySet(props, "StartArguments", options.Args);
            TrySet(props, "StartWorkingDirectory", options.Cwd);

            if (options.Env is { Count: > 0 })
            {
                var serialized = string.Join("\n", System.Linq.Enumerable.Select(options.Env, kv => $"{kv.Key}={kv.Value}"));
                TrySet(props, "EnvironmentVariables", serialized);
            }
        }
        catch { }
    }

    private static void TrySet(EnvDTE.Properties props, string name, string? value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (value is null) return;
        try { props.Item(name).Value = value; } catch { }
    }
}
