using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    private const int DefaultEvalTimeoutMs = 5000;
    private static readonly Guid s_filterRegisters = new("223ae797-bd09-4f28-8241-2763bdc5f713"); // guidFilterRegisters

    public async Task<MemoryReadResult> MemoryReadAsync(string address, int length, CancellationToken cancellationToken = default)
    {
        if (length <= 0) throw new VsmcpException(ErrorCodes.NotFound, "Length must be > 0.");
        if (length > 64 * 1024) throw new VsmcpException(ErrorCodes.WrongState, "Reads are capped at 64 KiB per call.");
        var addrVal = ParseAddress(address);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        _ = RequireDebugging(dte);

        var (mem, ctx) = GetMemoryBytesAndContext(addrVal);
        var buffer = new byte[length];
        uint read = 0;
        uint unreadable = 0;
        var hr = mem.ReadAt(ctx, (uint)length, buffer, out read, ref unreadable);
        if (hr != VSConstants.S_OK && read == 0)
            throw new VsmcpException(ErrorCodes.InteropFault, $"ReadAt returned HRESULT 0x{hr:X8}.");

        var actual = (int)read;
        var slice = new byte[actual];
        Array.Copy(buffer, slice, actual);

        return new MemoryReadResult
        {
            Address = "0x" + addrVal.ToString("X"),
            RequestedBytes = length,
            ReadBytes = actual,
            UnreadableBytes = (int)unreadable,
            Hex = ToHex(slice),
            Base64 = Convert.ToBase64String(slice),
        };
    }

    public async Task<MemoryWriteResult> MemoryWriteAsync(string address, string hex, bool allowSideEffects, CancellationToken cancellationToken = default)
    {
        if (!allowSideEffects)
            throw new VsmcpException(ErrorCodes.WrongState, "memory.write changes process state. Pass allowSideEffects=true to proceed.");
        if (string.IsNullOrWhiteSpace(hex)) throw new VsmcpException(ErrorCodes.NotFound, "hex payload is required.");
        var bytes = ParseHex(hex);
        if (bytes.Length == 0) throw new VsmcpException(ErrorCodes.NotFound, "hex payload decoded to zero bytes.");
        if (bytes.Length > 64 * 1024) throw new VsmcpException(ErrorCodes.WrongState, "Writes are capped at 64 KiB per call.");
        var addrVal = ParseAddress(address);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        _ = RequireDebugging(dte);

        var (mem, ctx) = GetMemoryBytesAndContext(addrVal);
        var hr = mem.WriteAt(ctx, (uint)bytes.Length, bytes);
        if (hr != VSConstants.S_OK)
            throw new VsmcpException(ErrorCodes.InteropFault, $"WriteAt returned HRESULT 0x{hr:X8}.");

        return new MemoryWriteResult
        {
            Address = "0x" + addrVal.ToString("X"),
            WrittenBytes = bytes.Length,
        };
    }

    public async Task<RegistersResult> RegistersGetAsync(int? threadId, int? frameIndex, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var dteThread = threadId is int tid
            ? (FindThread(debugger, tid) ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {tid}."))
            : debugger.CurrentThread ?? throw new VsmcpException(ErrorCodes.WrongState, "No current thread. Break into the debuggee first.");

        var idx = frameIndex ?? TryGetCurrentFrameIndex(debugger, dteThread);
        if (idx < 0) idx = 0;

        var frame = RequireSdkFrame(dteThread.ID, idx);
        var result = new RegistersResult { ThreadId = dteThread.ID, FrameIndex = idx };

        // Try filtered enumeration first (the proper path — returns register groups as children).
        var filter = s_filterRegisters;
        if (frame.EnumProperties(
                enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME
                | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE
                | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE
                | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB,
                10,
                ref filter,
                (uint)DefaultEvalTimeoutMs,
                out uint _,
                out IEnumDebugPropertyInfo2? groupEnum) != VSConstants.S_OK || groupEnum is null)
        {
            return result;
        }

        var groupInfo = new DEBUG_PROPERTY_INFO[1];
        uint fetched;
        while (groupEnum.Next(1, groupInfo, out fetched) == VSConstants.S_OK && fetched == 1)
        {
            var group = new RegisterGroupInfo { Name = groupInfo[0].bstrName ?? "" };
            var prop = groupInfo[0].pProperty;
            if (prop is not null)
            {
                var children = EnumChildren(prop);
                foreach (var r in children)
                    group.Registers.Add(r);
            }
            if (group.Registers.Count > 0 || !string.IsNullOrEmpty(group.Name))
                result.Groups.Add(group);
        }
        return result;
    }

    public async Task<DisasmResult> DisasmGetAsync(string address, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0) throw new VsmcpException(ErrorCodes.NotFound, "Count must be > 0.");
        if (count > 4096) throw new VsmcpException(ErrorCodes.WrongState, "Disasm is capped at 4096 instructions per call.");
        var addrVal = ParseAddress(address);

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        _ = RequireDebugging(dte);

        var program = _package.Modules?.CurrentProgram
            ?? throw new VsmcpException(ErrorCodes.NotDebugging, "No active debug program. Start/attach a debug session first.");

        // Obtain a memory context at the requested address, then cast to IDebugCodeContext2 for disasm.
        var (_, memCtx) = GetMemoryBytesAndContext(addrVal);
        if (memCtx is not IDebugCodeContext2 codeCtx)
            throw new VsmcpException(ErrorCodes.Unsupported, "Debug engine did not return a code context for the requested address.");

        if (program.GetDisassemblyStream(
                enum_DISASSEMBLY_STREAM_SCOPE.DSS_ALL,
                codeCtx,
                out IDebugDisassemblyStream2? stream) != VSConstants.S_OK || stream is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Engine could not create a disassembly stream at the requested address.");

        var instructions = new DisassemblyData[count];
        uint got = 0;
        var fields =
            enum_DISASSEMBLY_STREAM_FIELDS.DSF_ADDRESS
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_CODEBYTES
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_OPCODE
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_OPERANDS_SYMBOLS
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_SYMBOL
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_DOCUMENTURL
            | enum_DISASSEMBLY_STREAM_FIELDS.DSF_POSITION;

        var hr = stream.Read((uint)count, fields, out got, instructions);
        if (hr != VSConstants.S_OK && got == 0)
            throw new VsmcpException(ErrorCodes.InteropFault, $"Disassembly Read returned HRESULT 0x{hr:X8}.");

        var result = new DisasmResult
        {
            StartAddress = "0x" + addrVal.ToString("X"),
            RequestedCount = count,
        };

        for (int i = 0; i < (int)got; i++)
        {
            var d = instructions[i];
            result.Instructions.Add(new DisasmInstruction
            {
                Address = string.IsNullOrEmpty(d.bstrAddress) ? "" : d.bstrAddress,
                Bytes = string.IsNullOrEmpty(d.bstrCodeBytes) ? null : d.bstrCodeBytes,
                Opcode = string.IsNullOrEmpty(d.bstrOpcode) ? null : d.bstrOpcode,
                Operands = string.IsNullOrEmpty(d.bstrOperands) ? null : d.bstrOperands,
                Symbol = string.IsNullOrEmpty(d.bstrSymbol) ? null : d.bstrSymbol,
                File = string.IsNullOrEmpty(d.bstrDocumentUrl) ? null : d.bstrDocumentUrl,
                Line = d.posBeg.dwLine > 0 ? (int)d.posBeg.dwLine + 1 : (int?)null,
            });
        }
        return result;
    }

    // -------- helpers --------

    private static List<RegisterInfo> EnumChildren(IDebugProperty2 prop)
    {
        var list = new List<RegisterInfo>();
        var filter = Guid.Empty;
        if (prop.EnumChildren(
                enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME
                | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE
                | enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE,
                10,
                ref filter,
                enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_ALL,
                null,
                (uint)DefaultEvalTimeoutMs,
                out IEnumDebugPropertyInfo2? childEnum) != VSConstants.S_OK || childEnum is null)
            return list;

        var buf = new DEBUG_PROPERTY_INFO[1];
        while (childEnum.Next(1, buf, out uint got) == VSConstants.S_OK && got == 1)
        {
            var info = buf[0];
            list.Add(new RegisterInfo
            {
                Name = info.bstrName ?? "",
                Value = info.bstrValue ?? "",
                Type = string.IsNullOrEmpty(info.bstrType) ? null : info.bstrType,
            });
        }
        return list;
    }

    private IDebugStackFrame2 RequireSdkFrame(int threadId, int frameIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var program = _package.Modules?.CurrentProgram
            ?? throw new VsmcpException(ErrorCodes.NotDebugging, "No active debug program.");
        if (program.EnumThreads(out IEnumDebugThreads2? threadEnum) != VSConstants.S_OK || threadEnum is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "EnumThreads failed.");

        var tbuf = new IDebugThread2[1];
        uint gt = 0;
        while (threadEnum.Next(1, tbuf, ref gt) == VSConstants.S_OK && gt == 1)
        {
            if (tbuf[0].GetThreadId(out uint tid) != VSConstants.S_OK) continue;
            if ((int)tid != threadId) continue;

            if (tbuf[0].EnumFrameInfo(
                    enum_FRAMEINFO_FLAGS.FIF_FRAME | enum_FRAMEINFO_FLAGS.FIF_FUNCNAME,
                    0,
                    out IEnumDebugFrameInfo2? frameEnum) != VSConstants.S_OK || frameEnum is null)
                continue;

            var fbuf = new FRAMEINFO[1];
            uint gf = 0;
            int i = 0;
            while (frameEnum.Next(1, fbuf, ref gf) == VSConstants.S_OK && gf == 1)
            {
                if (i == frameIndex && fbuf[0].m_pFrame is not null) return fbuf[0].m_pFrame;
                i++;
            }
            throw new VsmcpException(ErrorCodes.NotFound, $"Frame {frameIndex} not found on thread {threadId}.");
        }
        throw new VsmcpException(ErrorCodes.NotFound, $"Thread {threadId} not found in the active program.");
    }

    private (IDebugMemoryBytes2 mem, IDebugMemoryContext2 ctx) GetMemoryBytesAndContext(ulong address)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var program = _package.Modules?.CurrentProgram
            ?? throw new VsmcpException(ErrorCodes.NotDebugging, "No active debug program.");
        if (program.GetMemoryBytes(out IDebugMemoryBytes2? mem) != VSConstants.S_OK || mem is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Engine did not provide memory access (GetMemoryBytes failed).");

        // Build a memory context at `address` by evaluating a pointer-cast expression against the current frame.
        var ctx = ResolveMemoryContext(address);
        return (mem, ctx);
    }

    private IDebugMemoryContext2 ResolveMemoryContext(ulong address)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");
        var debugger = RequireDebugging(dte);
        var dteThread = debugger.CurrentThread
            ?? throw new VsmcpException(ErrorCodes.WrongState, "A current thread is required to address memory.");
        var frameIdx = TryGetCurrentFrameIndex(debugger, dteThread);
        var sdkFrame = RequireSdkFrame(dteThread.ID, Math.Max(0, frameIdx));

        if (sdkFrame.GetExpressionContext(out IDebugExpressionContext2? exprCtx) != VSConstants.S_OK || exprCtx is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Could not obtain expression context on the current frame.");

        var expression = $"(unsigned char*)0x{address:X}";
        if (exprCtx.ParseText(expression, enum_PARSEFLAGS.PARSE_EXPRESSION, 10, out IDebugExpression2? expr, out string err, out uint _) != VSConstants.S_OK || expr is null)
            throw new VsmcpException(ErrorCodes.InteropFault, $"Could not parse address expression ({err ?? "unknown"}). Memory ops currently require a native/C++ frame.");

        if (expr.EvaluateSync(enum_EVALFLAGS.EVAL_NOSIDEEFFECTS, (uint)DefaultEvalTimeoutMs, null, out IDebugProperty2? prop) != VSConstants.S_OK || prop is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Evaluation of address expression failed.");

        if (prop.GetMemoryContext(out IDebugMemoryContext2? ctx) != VSConstants.S_OK || ctx is null)
            throw new VsmcpException(ErrorCodes.InteropFault, "Property did not yield a memory context.");

        return ctx;
    }

    private static ulong ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new VsmcpException(ErrorCodes.NotFound, "Address is required.");
        var s = address.Trim();
        var style = NumberStyles.Integer;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(2);
            style = NumberStyles.HexNumber;
        }
        if (!ulong.TryParse(s, style, CultureInfo.InvariantCulture, out var v))
            throw new VsmcpException(ErrorCodes.NotFound, $"Could not parse address '{address}'. Use decimal or 0x-prefixed hex.");
        return v;
    }

    private static byte[] ParseHex(string hex)
    {
        var sb = new StringBuilder(hex.Length);
        foreach (var c in hex) if (!char.IsWhiteSpace(c) && c != '-' && c != ',' && c != ':') sb.Append(c);
        var s = sb.ToString();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        if ((s.Length & 1) != 0) throw new VsmcpException(ErrorCodes.NotFound, "Hex payload must have an even number of nibbles.");
        var bytes = new byte[s.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                throw new VsmcpException(ErrorCodes.NotFound, $"Invalid hex byte near position {i * 2}.");
        }
        return bytes;
    }

    private static string ToHex(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
