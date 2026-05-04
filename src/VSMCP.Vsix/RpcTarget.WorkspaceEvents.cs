using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<WorkspaceEventsResult> WorkspaceEventsListAsync(
        int maxResults, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var w = _package.WorkspaceEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "WorkspaceWatcher not initialised.");
        return w.GetEvents(maxResults <= 0 ? 100 : maxResults);
    }

    public async Task<WorkspaceEventsResult> WorkspaceWatchAsync(
        long sinceTimestampMs, int timeoutMs, int maxResults,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var w = _package.WorkspaceEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "WorkspaceWatcher not initialised.");
        return await w.WaitForEventsAsync(
            sinceTimestampMs,
            timeoutMs <= 0 ? 10_000 : timeoutMs,
            maxResults <= 0 ? 50 : maxResults,
            cancellationToken).ConfigureAwait(false);
    }
}
