using System;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Covers the typed error model (#126): VsmcpException now lives in VSMCP.Shared and
/// RpcError.FromException must recover the code whether it sees the real exception type or
/// only the "{code}: {message}" string a deserialized RemoteInvocationException carries.
/// </summary>
public class RpcErrorTests
{
    [Fact]
    public void FromException_TypedVsmcpException_KeepsCodeAndStripsPrefix()
    {
        var ex = new VsmcpException(ErrorCodes.NotFound, "File not part of any loaded project: x.cs");
        var err = RpcError.FromException(ex);

        Assert.Equal(ErrorCodes.NotFound, err.Code);
        Assert.Equal("File not part of any loaded project: x.cs", err.Message);
        Assert.DoesNotContain("VSMCP-", err.Message);
    }

    [Fact]
    public void FromException_PlainExceptionWithCodePrefix_ParsesCode()
    {
        // Simulates what the client observes after StreamJsonRpc reduces the remote
        // VsmcpException to a generic exception whose Message is the formatted envelope.
        var ex = new InvalidOperationException("VSMCP-not-connected: no running Visual Studio instance");
        var err = RpcError.FromException(ex);

        Assert.Equal(ErrorCodes.NotConnected, err.Code);
        Assert.Equal("no running Visual Studio instance", err.Message);
    }

    [Fact]
    public void FromException_UnrecognizedException_FallsBackToInteropFault()
    {
        var err = RpcError.FromException(new Exception("something went sideways"));

        Assert.Equal(ErrorCodes.InteropFault, err.Code);
        Assert.Equal("something went sideways", err.Message);
    }

    [Fact]
    public void FromException_CapturesInnerExceptionAsDetail()
    {
        var ex = new VsmcpException(ErrorCodes.InteropFault, "wrapped", new InvalidOperationException("root cause"));
        var err = RpcError.FromException(ex);

        Assert.Equal("root cause", err.Detail);
    }

    [Fact]
    public void VsmcpException_FormatsMessageWithCodePrefix()
    {
        var ex = new VsmcpException(ErrorCodes.WrongState, "not in break mode");
        Assert.StartsWith("VSMCP-wrong-state: ", ex.Message);
    }
}
