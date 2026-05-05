namespace VSMCP.Shared;

/// <summary>RPC protocol version — bumped on breaking contract changes.</summary>
public static class ProtocolVersion
{
    public const int Major = 0;
    public const int Minor = 22;
    public static string DisplayString => $"{Major}.{Minor}";
}
