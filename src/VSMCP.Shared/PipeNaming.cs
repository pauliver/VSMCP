using System.Diagnostics;

namespace VSMCP.Shared;

public static class PipeNaming
{
    public const string Prefix = "VSMCP.";
    public const string CppPrefix = "VSMCP.Cpp.";

    public static string ForProcess(int pid) => $"{Prefix}{pid}";

    public static string ForCurrentProcess() => ForProcess(Process.GetCurrentProcess().Id);

    public static bool IsVsmcpPipe(string name) => name.StartsWith(Prefix, System.StringComparison.Ordinal);

    /// <summary>Pipe name for the CppAnalyzer sidecar that the given VS process owns.</summary>
    public static string ForCppAnalyzer(int vsPid) => $"{CppPrefix}{vsPid}";
}
