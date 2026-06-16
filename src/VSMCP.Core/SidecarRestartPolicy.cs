using System.Collections.Generic;

namespace VSMCP.Core;

/// <summary>
/// Circuit-breaker for respawning the out-of-process C++ analyzer sidecar (#131). The host
/// auto-restarts the sidecar when it dies, but a sidecar that crashes on every start would
/// otherwise spin forever. This bounds restarts to <c>maxStartsPerWindow</c> within a rolling
/// <c>windowMs</c>; once tripped, the host surfaces a clear error instead of looping.
///
/// Pure and time-injected (caller passes <c>nowMs</c>) so it is unit-testable without real
/// clocks or a live sidecar. Not internally synchronized — callers serialize access.
/// </summary>
public sealed class SidecarRestartPolicy
{
    private readonly int _maxStartsPerWindow;
    private readonly long _windowMs;
    private readonly Queue<long> _starts = new();

    public SidecarRestartPolicy(int maxStartsPerWindow = 3, long windowMs = 60_000)
    {
        _maxStartsPerWindow = maxStartsPerWindow;
        _windowMs = windowMs;
    }

    /// <summary>
    /// Record a (re)start attempt at <paramref name="nowMs"/>. Returns false — without
    /// recording — when the breaker is open (too many starts in the window).
    /// </summary>
    public bool TryRegisterStart(long nowMs)
    {
        Trim(nowMs);
        if (_starts.Count >= _maxStartsPerWindow) return false;
        _starts.Enqueue(nowMs);
        return true;
    }

    /// <summary>Number of starts still inside the rolling window as of <paramref name="nowMs"/>.</summary>
    public int RecentStartCount(long nowMs)
    {
        Trim(nowMs);
        return _starts.Count;
    }

    private void Trim(long nowMs)
    {
        while (_starts.Count > 0 && nowMs - _starts.Peek() > _windowMs) _starts.Dequeue();
    }
}
