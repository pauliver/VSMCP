using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<DumpOpenResult> DumpOpenAsync(DumpOpenOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new VsmcpException(ErrorCodes.NotFound, "Options are required.");
        if (string.IsNullOrWhiteSpace(options.Path))
            throw new VsmcpException(ErrorCodes.NotFound, "Dump path is required.");
        var path = Path.GetFullPath(options.Path);
        if (!File.Exists(path))
            throw new VsmcpException(ErrorCodes.NotFound, $"Dump file not found: {path}");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        // Opening a .dmp through ItemOperations routes it to the Dump Summary editor.
        // Debug.Start then invokes the Mini-Dump Debug Engine on the opened document.
        try
        {
            dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindPrimary);
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to open dump document: {ex.Message}", ex);
        }

        try
        {
            dte.ExecuteCommand("Debug.Start");
        }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.InteropFault, $"Dump opened, but Debug.Start failed: {ex.Message}", ex);
        }

        var result = new DumpOpenResult { Path = path };
        try
        {
            var debugger = dte.Debugger;
            if (debugger is not null)
            {
                try { result.FaultingThreadId = debugger.CurrentThread?.ID; } catch { }
                try
                {
                    var expr = debugger.GetExpression("$exception", true, 200);
                    if (expr is not null && expr.IsValidValue && !string.IsNullOrEmpty(expr.Value))
                        result.ExceptionMessage = expr.Value;
                }
                catch { }
            }
        }
        catch { }

        try { result.ModuleCount = _package.Modules?.Snapshot().Count ?? 0; } catch { }
        return result;
    }

    public async Task<DumpSummaryResult> DumpSummaryAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = dte.Debugger
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "DTE.Debugger unavailable.");

        var result = new DumpSummaryResult();
        result.Mode = debugger.CurrentMode switch
        {
            EnvDTE.dbgDebugMode.dbgBreakMode => DebugMode.Break,
            EnvDTE.dbgDebugMode.dbgRunMode => DebugMode.Run,
            _ => DebugMode.Design,
        };

        try
        {
            var proc = debugger.CurrentProcess;
            if (proc is not null)
            {
                result.ProcessId = proc.ProcessID;
                result.ProcessName = proc.Name;
            }
        }
        catch { }

        try
        {
            var thread = debugger.CurrentThread;
            if (thread is not null)
            {
                result.FaultingThreadId = thread.ID;
                result.FaultingThreadName = thread.Name;
            }
        }
        catch { }

        try
        {
            var ex = debugger.GetExpression("$exception", true, 200);
            if (ex is not null && ex.IsValidValue && !string.IsNullOrEmpty(ex.Value))
                result.ExceptionMessage = ex.Value;
        }
        catch { }

        var modules = _package.Modules?.Snapshot();
        if (modules is not null)
        {
            foreach (var m in modules)
            {
                result.Modules.Add(m);
                if (LooksManaged(m)) result.ManagedModuleCount++;
                else result.NativeModuleCount++;
            }
            result.ModuleCount = result.Modules.Count;
        }
        return result;
    }

    public async Task<DumpSaveResult> DumpSaveAsync(DumpSaveOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new VsmcpException(ErrorCodes.NotFound, "Options are required.");
        if (options.Pid <= 0) throw new VsmcpException(ErrorCodes.NotFound, "Pid must be > 0.");
        if (string.IsNullOrWhiteSpace(options.Path))
            throw new VsmcpException(ErrorCodes.NotFound, "Destination path is required.");

        var destPath = Path.GetFullPath(options.Path);
        var parent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            throw new VsmcpException(ErrorCodes.NotFound, $"Parent directory does not exist: {parent}");

        // Dump capture does not touch VS state, so we don't need the UI thread; just run it.
        return await Task.Run(() => WriteDumpSync(options.Pid, destPath, options.Full), cancellationToken);
    }

    // -------- helpers --------

    private static DumpSaveResult WriteDumpSync(int pid, string destPath, bool full)
    {
        IntPtr processHandle = IntPtr.Zero;
        FileStream? file = null;
        try
        {
            processHandle = OpenProcess(ProcessAccess.QueryInformation | ProcessAccess.VmRead, false, (uint)pid);
            if (processHandle == IntPtr.Zero)
                throw new VsmcpException(ErrorCodes.NotFound, $"OpenProcess({pid}) failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var flags = full ? FullDumpFlags : MinidumpFlags;
            if (!MiniDumpWriteDump(processHandle, (uint)pid, file.SafeFileHandle.DangerousGetHandle(), (uint)flags, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                throw new VsmcpException(ErrorCodes.InteropFault, $"MiniDumpWriteDump failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            file.Flush(true);
            var bytes = file.Length;
            return new DumpSaveResult { Path = destPath, BytesWritten = bytes, Full = full };
        }
        finally
        {
            file?.Dispose();
            if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
        }
    }

    private static bool LooksManaged(ModuleInfo m)
    {
        // Best-effort classification: managed modules typically end in .dll and show up in the CLR/.NET engine.
        // We don't have MIF_MANAGED, so fall back to filename heuristics + symbol state hints.
        var name = m.Name ?? "";
        if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // -------- P/Invoke (dbghelp + kernel32) --------

    [Flags]
    private enum ProcessAccess : uint
    {
        QueryInformation = 0x0400,
        VmRead = 0x0010,
        DupHandle = 0x0040,
        AllAccess = 0x001F0FFF,
    }

    // MINIDUMP_TYPE values (subset). Full = comprehensive heap + handle + unloaded + thread info + token + modules.
    private const uint MinidumpFlags =
        0x00000000 /* MiniDumpNormal */
        | 0x00000004 /* WithHandleData */
        | 0x00000040 /* WithUnloadedModules */
        | 0x00001000 /* WithThreadInfo */;

    private const uint FullDumpFlags =
        0x00000002 /* WithFullMemory */
        | 0x00000004 /* WithHandleData */
        | 0x00000040 /* WithUnloadedModules */
        | 0x00000800 /* WithFullMemoryInfo */
        | 0x00001000 /* WithThreadInfo */
        | 0x00002000 /* WithCodeSegs */
        | 0x00040000 /* WithTokenInformation */;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(ProcessAccess dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        IntPtr hFile,
        uint dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);
}
