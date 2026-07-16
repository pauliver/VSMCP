namespace VSMCP.Core;

/// <summary>
/// Decides which inbound RPCs must run under the host-wide serialization lock (audit through-line:
/// concurrency). Everything is serialized by default so overlapping tool calls can't interleave on
/// the shared VS state / single UI thread — the exceptions are trivial meta calls and long-poll
/// waiters that block for seconds-to-minutes awaiting an external event and so must NOT hold the lock
/// (holding it would head-of-line-block the entire tool surface).
///
/// The argument is <c>request.Method</c> as StreamJsonRpc dispatches it — the CLR method name incl.
/// the "Async" suffix (same value <c>AutoFocusJsonRpc.SkipFocus</c> already switches on). Pure so the
/// serialize-vs-exempt decision is one unit-testable knob rather than scattered across call sites.
/// </summary>
public static class RpcConcurrencyPolicy
{
    public static bool RequiresExclusive(string rpcMethodName)
    {
        switch (rpcMethodName)
        {
            // Trivial, state-free meta calls.
            case "HandshakeAsync":
            case "PingAsync":
            // Long-poll waiters: they await an external event and must not pin the host lock.
            case "BuildWaitAsync":
            case "DiagEventsWatchAsync":
            case "WorkspaceWatchAsync":
                return false;
            default:
                return true;
        }
    }
}
