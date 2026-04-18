using System.Diagnostics;

namespace VSMCP.Shared;

public static class PipeNaming
{
    public const string Prefix = "VSMCP.";

    public static string ForProcess(int pid) => $"{Prefix}{pid}";

    public static string ForCurrentProcess() => ForProcess(Process.GetCurrentProcess().Id);

    public static bool IsVsmcpPipe(string name) => name.StartsWith(Prefix, System.StringComparison.Ordinal);
}
