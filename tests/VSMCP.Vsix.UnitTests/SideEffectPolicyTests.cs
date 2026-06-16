using VSMCP.Core;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers the global side-effect ceiling (#133).</summary>
public class SideEffectPolicyTests
{
    [Fact]
    public void Allowed_when_requested_and_globally_enabled()
        => Assert.True(SideEffectPolicy.Enforce(requested: true, globallyAllowed: true, "eval.expression"));

    [Fact]
    public void False_when_not_requested_regardless_of_global()
    {
        Assert.False(SideEffectPolicy.Enforce(false, true, "memory.write"));
        Assert.False(SideEffectPolicy.Enforce(false, false, "memory.write"));
    }

    [Fact]
    public void Throws_when_requested_but_globally_disabled()
    {
        var ex = Assert.Throws<VsmcpException>(() => SideEffectPolicy.Enforce(true, false, "debug.set_next_statement"));
        Assert.Equal(ErrorCodes.WrongState, ex.Code);
        Assert.Contains("debug.set_next_statement", ex.Message);
    }
}
