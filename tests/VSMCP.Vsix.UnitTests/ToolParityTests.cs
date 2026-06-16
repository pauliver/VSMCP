using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Contract test for issue #127: the MCP tool layer (VsmcpTools*.cs) is a hand-written
/// mirror of IVsmcpRpc, with no compile-time link between the two. The prior audit found a
/// snapshot where ~72 RPC methods had no tool wrapper at all. This test fails CI the moment
/// an IVsmcpRpc method ships without a tool that reaches it, closing that drift class.
///
/// Invariant: every IVsmcpRpc method is reachable from at least one tool — i.e. its name is
/// invoked as `proxy.&lt;Method&gt;(` somewhere in VsmcpTools*.cs — except for an explicit,
/// justified allowlist of methods that are intentionally internal or exposed only indirectly.
/// </summary>
public class ToolParityTests
{
    // Methods that are deliberately NOT called directly by a tool. Each needs a reason.
    private static readonly Dictionary<string, string> Allowlist = new()
    {
        ["HandshakeAsync"] = "Connection meta — invoked by VsConnection during connect, never a tool.",
        ["CppSetPchHeaderAsync"] = "Internal libclang PCH mechanism, driven by RpcTarget, not user-facing.",
        ["CppSetUnsavedBufferAsync"] = "Exposed only via the batch tool cpp.set_unsaved_buffer_many.",
        ["FileReadManyAsync"] = "Superseded by file.read_many, which batches FileReadAsync server-side.",
    };

    [Fact]
    public void EveryRpcMethodIsReachableFromATool()
    {
        var root = RepoRoot();
        var interfaceMethods = ExtractInterfaceMethods(Path.Combine(root, "src", "VSMCP.Shared", "IVsmcpRpc.cs"));
        var calledMethods = ExtractCalledProxyMethods(Path.Combine(root, "src", "VSMCP.Server"));

        Assert.True(interfaceMethods.Count > 200, $"Sanity: expected to parse the full interface, got {interfaceMethods.Count}.");

        var missing = interfaceMethods
            .Where(m => !Allowlist.ContainsKey(m) && !calledMethods.Contains(m))
            .OrderBy(m => m)
            .ToList();

        Assert.True(missing.Count == 0,
            "IVsmcpRpc methods with no MCP tool that reaches them (add a [McpServerTool] in " +
            "VsmcpTools*.cs, or add to the allowlist with a reason):\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void AllowlistHasNoStaleEntries()
    {
        var root = RepoRoot();
        var interfaceMethods = ExtractInterfaceMethods(Path.Combine(root, "src", "VSMCP.Shared", "IVsmcpRpc.cs"));

        var unknown = Allowlist.Keys.Where(m => !interfaceMethods.Contains(m)).OrderBy(m => m).ToList();
        Assert.True(unknown.Count == 0,
            "Allowlist names that are not (or no longer) IVsmcpRpc methods — remove them:\n  " + string.Join("\n  ", unknown));
    }

    private static readonly Regex MethodDecl = new(@"\b(?<name>[A-Z][A-Za-z0-9]*Async)\s*\(", RegexOptions.Compiled);
    private static readonly Regex ProxyCall = new(@"\.(?<name>[A-Z][A-Za-z0-9]*Async)\s*\(", RegexOptions.Compiled);

    private static HashSet<string> ExtractInterfaceMethods(string ifacePath)
    {
        var text = File.ReadAllText(ifacePath);
        return new HashSet<string>(MethodDecl.Matches(text).Cast<Match>().Select(m => m.Groups["name"].Value));
    }

    private static HashSet<string> ExtractCalledProxyMethods(string serverDir)
    {
        var set = new HashSet<string>();
        foreach (var file in Directory.GetFiles(serverDir, "VsmcpTools*.cs"))
        foreach (Match m in ProxyCall.Matches(File.ReadAllText(file)))
            set.Add(m.Groups["name"].Value);
        return set;
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (dir != null && !File.Exists(Path.Combine(dir, "src", "VSMCP.Shared", "IVsmcpRpc.cs")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root from " + thisFile);
    }
}
