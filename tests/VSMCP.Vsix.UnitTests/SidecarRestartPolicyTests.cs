using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers the sidecar restart circuit-breaker (#131).</summary>
public class SidecarRestartPolicyTests
{
    [Fact]
    public void Allows_up_to_max_starts_then_trips()
    {
        var p = new SidecarRestartPolicy(maxStartsPerWindow: 3, windowMs: 60_000);
        Assert.True(p.TryRegisterStart(1000));
        Assert.True(p.TryRegisterStart(2000));
        Assert.True(p.TryRegisterStart(3000));
        Assert.False(p.TryRegisterStart(4000));   // breaker open
    }

    [Fact]
    public void Recovers_after_window_elapses()
    {
        var p = new SidecarRestartPolicy(maxStartsPerWindow: 2, windowMs: 10_000);
        Assert.True(p.TryRegisterStart(0));
        Assert.True(p.TryRegisterStart(5_000));
        Assert.False(p.TryRegisterStart(6_000));        // 2 within 10s
        Assert.True(p.TryRegisterStart(20_000));        // first two aged out
    }

    [Fact]
    public void RecentStartCount_drops_aged_entries()
    {
        var p = new SidecarRestartPolicy(maxStartsPerWindow: 5, windowMs: 1_000);
        p.TryRegisterStart(0);
        p.TryRegisterStart(500);
        Assert.Equal(2, p.RecentStartCount(800));
        Assert.Equal(0, p.RecentStartCount(2_000));
    }

    [Fact]
    public void Denied_start_is_not_recorded()
    {
        var p = new SidecarRestartPolicy(maxStartsPerWindow: 1, windowMs: 60_000);
        Assert.True(p.TryRegisterStart(0));
        Assert.False(p.TryRegisterStart(1));
        Assert.False(p.TryRegisterStart(2));   // still denied; the denials didn't fill the window further
        Assert.Equal(1, p.RecentStartCount(3));
    }
}
