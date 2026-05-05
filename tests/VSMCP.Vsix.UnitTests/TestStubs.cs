using System;

namespace VSMCP.Vsix;

/// <summary>
/// Test-only stub matching the internal VsmcpException defined in VSMCP.Vsix/VsHelpers.cs.
/// Linking VsHelpers.cs would pull in the VS SDK; we just need the type to satisfy
/// CppOutlineParser's compile, since the parser only throws on file-not-found which
/// our tests never hit.
/// </summary>
internal sealed class VsmcpException : Exception
{
    public string Code { get; }
    public VsmcpException(string code, string message) : base($"{code}: {message}") => Code = code;
    public VsmcpException(string code, string message, Exception inner) : base($"{code}: {message}", inner) => Code = code;
}
