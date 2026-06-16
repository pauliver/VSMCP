using System;
using System.Text.RegularExpressions;

namespace VSMCP.Shared;

/// <summary>
/// The single exception type VSMCP throws across the bridge. Lives in VSMCP.Shared (not the
/// VSIX) so both sides — and any client — can reference it and the <see cref="Code"/> by type.
/// The message is formatted as "{code}: {message}" so the code survives StreamJsonRpc
/// serialization even when the concrete type is reduced to a RemoteInvocationException on the
/// far side; <see cref="RpcError.FromException"/> recovers the code from that string.
/// </summary>
public sealed class VsmcpException : Exception
{
    /// <summary>A stable <c>VSMCP-*</c> code from <see cref="ErrorCodes"/>.</summary>
    public string Code { get; }

    public VsmcpException(string code, string message) : base($"{code}: {message}") => Code = code;
    public VsmcpException(string code, string message, Exception inner) : base($"{code}: {message}", inner) => Code = code;
}

/// <summary>
/// A structured, wire-friendly error the MCP client can branch on by <see cref="Code"/> rather
/// than string-sniffing. Build one from any caught exception with <see cref="FromException"/>.
/// </summary>
public sealed class RpcError
{
    public string Code { get; set; } = ErrorCodes.InteropFault;
    public string Message { get; set; } = "";
    public string? Detail { get; set; }

    // Matches the "{code}: {message}" envelope produced by VsmcpException, including when it
    // arrives as a deserialized RemoteInvocationException message on the client side.
    private static readonly Regex CodePrefix = new(
        @"^(?<code>VSMCP-[a-z][a-z-]*)\s*:\s*(?<msg>.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Recover a typed error from any exception. Prefers a real <see cref="VsmcpException"/>;
    /// otherwise parses the "{code}: {message}" prefix (e.g. from a StreamJsonRpc
    /// RemoteInvocationException); otherwise falls back to <see cref="ErrorCodes.InteropFault"/>.
    /// </summary>
    public static RpcError FromException(Exception ex)
    {
        if (ex is null) throw new ArgumentNullException(nameof(ex));

        if (ex is VsmcpException ve)
            return new RpcError { Code = ve.Code, Message = StripCode(ve.Message, ve.Code), Detail = ex.InnerException?.Message };

        var m = CodePrefix.Match(ex.Message ?? "");
        if (m.Success)
            return new RpcError { Code = m.Groups["code"].Value, Message = m.Groups["msg"].Value, Detail = ex.InnerException?.Message };

        return new RpcError { Code = ErrorCodes.InteropFault, Message = ex.Message ?? ex.GetType().Name, Detail = ex.InnerException?.Message };
    }

    private static string StripCode(string message, string code)
    {
        var prefix = code + ": ";
        return message.StartsWith(prefix, StringComparison.Ordinal) ? message.Substring(prefix.Length) : message;
    }

    public override string ToString() => $"{Code}: {Message}";
}
