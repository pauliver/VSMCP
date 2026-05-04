using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Standalone CLI mode (issue #70). Lets you invoke any MCP tool from a shell
/// without an MCP client — ideal for quick scripts and CI checks.
///
///   vsmcp serve                 — MCP stdio server (default)
///   vsmcp instances             — list running VS instances with VSMCP loaded
///   vsmcp tools                 — list every MCP tool name and description
///   vsmcp call &lt;tool&gt; [--json '{"arg":"value"}']
///                                — invoke a tool with JSON-shaped arguments
///   vsmcp help [tool]           — describe a tool's parameters
/// </summary>
internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "instances" => HandleInstances(),
                "tools" => HandleTools(),
                "call" => await HandleCallAsync(args, ct).ConfigureAwait(false),
                "help" or "--help" or "-h" => HandleHelp(args),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vsmcp: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int HandleInstances()
    {
        var list = VsConnection.ListInstances();
        Console.WriteLine(JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int HandleTools()
    {
        var tools = DiscoverTools();
        foreach (var t in tools.OrderBy(t => t.Name))
            Console.WriteLine($"{t.Name,-32}  {Truncate(t.Description, 110)}");
        return 0;
    }

    private static int HandleHelp(string[] args)
    {
        if (args.Length < 2) { PrintUsage(); return 0; }
        var target = args[1];
        var tool = DiscoverTools().FirstOrDefault(t => t.Name == target);
        if (tool is null) { Console.Error.WriteLine($"vsmcp: unknown tool '{target}'."); return 1; }
        Console.WriteLine($"{tool.Name}");
        Console.WriteLine();
        Console.WriteLine($"  {tool.Description}");
        Console.WriteLine();
        Console.WriteLine("Parameters:");
        foreach (var p in tool.Method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var optional = p.HasDefaultValue ? " (optional)" : "";
            Console.WriteLine($"  {p.Name}: {p.ParameterType.Name}{optional}");
            if (!string.IsNullOrEmpty(desc)) Console.WriteLine($"    {desc}");
        }
        return 0;
    }

    private static async Task<int> HandleCallAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("vsmcp: 'call' requires a tool name. Try `vsmcp tools`.");
            return 1;
        }
        var name = args[1];
        var tool = DiscoverTools().FirstOrDefault(t => t.Name == name);
        if (tool is null) { Console.Error.WriteLine($"vsmcp: unknown tool '{name}'."); return 1; }

        // Parse --json argument.
        Dictionary<string, JsonElement> argMap = new(StringComparer.Ordinal);
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length)
            {
                var doc = JsonDocument.Parse(args[i + 1]);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    argMap[prop.Name] = prop.Value.Clone();
                i++;
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // --argname value : try parsing value as JSON first (numbers, bools, arrays,
                // objects all work). Fall back to a quoted string when not valid JSON.
                var key = args[i].Substring(2);
                if (i + 1 < args.Length)
                {
                    var raw = args[i + 1];
                    JsonElement parsed;
                    try { parsed = JsonDocument.Parse(raw).RootElement.Clone(); }
                    catch { parsed = JsonDocument.Parse(JsonSerializer.Serialize(raw)).RootElement.Clone(); }
                    argMap[key] = parsed;
                    i++;
                }
            }
        }

        // Build the VsmcpTools instance.
        await using var connection = new VsConnection();
        var profiler = new ProfilerHost();
        var counters = new CountersSubscriptionHost();
        var trace = new TraceHost();
        var config = VsmcpConfig.Load();
        var tools = new VsmcpTools(connection, profiler, counters, trace, config);

        // Bind parameters from argMap.
        var parameters = tool.Method.GetParameters();
        var values = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(CancellationToken)) { values[i] = ct; continue; }
            if (argMap.TryGetValue(p.Name!, out var je))
            {
                values[i] = JsonSerializer.Deserialize(je.GetRawText(), p.ParameterType);
            }
            else if (p.HasDefaultValue)
            {
                values[i] = p.DefaultValue;
            }
            else
            {
                Console.Error.WriteLine($"vsmcp: missing required argument '{p.Name}'.");
                return 1;
            }
        }

        var result = tool.Method.Invoke(tools, values);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var resProp = task.GetType().GetProperty("Result");
            var value = resProp?.GetValue(task);
            if (value is not null && value.GetType().Name != "VoidTaskResult")
                Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }

        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"vsmcp: unknown command '{cmd}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("vsmcp — Visual Studio MCP CLI");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  serve                            Run as MCP stdio server (default).");
        Console.Error.WriteLine("  instances                        List running VS instances with VSMCP.");
        Console.Error.WriteLine("  tools                            List every available MCP tool.");
        Console.Error.WriteLine("  call <tool> [--json '{...}']    Invoke a tool. Args are JSON.");
        Console.Error.WriteLine("  help <tool>                      Describe a tool's parameters.");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    private sealed record ToolEntry(string Name, string Description, MethodInfo Method);

    private static List<ToolEntry> DiscoverTools()
    {
        var result = new List<ToolEntry>();
        var attrType = typeof(McpServerToolAttribute);
        foreach (var t in typeof(VsmcpTools).Assembly.GetTypes())
        {
            if (t.GetCustomAttribute<McpServerToolTypeAttribute>() is null) continue;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = m.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is null) continue;
                var desc = m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                result.Add(new ToolEntry(attr.Name ?? m.Name, desc, m));
            }
        }
        return result;
    }
}
