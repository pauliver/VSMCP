using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSMCP.Server;

/// <summary>
/// Bridge-side configuration, loaded lazily from <c>%LOCALAPPDATA%\VSMCP\config.json</c>.
/// Missing fields fall back to defaults. The file is optional.
/// </summary>
public sealed class VsmcpConfig
{
    public string LogLevel { get; set; } = "warning";

    /// <summary>
    /// Global ceiling for side-effecting debug ops (eval/memory.write/set_next_statement).
    /// A tool's per-call allowSideEffects only applies when this is also true. Defaults to true
    /// (current behavior); set false to hard-disable side effects regardless of per-call flags.
    /// Enforced via <c>VSMCP.Core.SideEffectPolicy</c>.
    /// </summary>
    public bool AllowSideEffects { get; set; } = true;
    public bool AllowDbgEng { get; set; } = false;
    public int DefaultTimeoutMs { get; set; } = 30_000;

    /// <summary>File logging under <see cref="LogDirectory"/>. Default ON (at <see cref="LogLevel"/>,
    /// default warning) so a failure at a stranger's desk leaves a trace without a special build.</summary>
    public bool FileLoggingEnabled { get; set; } = true;

    /// <summary>Override the log directory. Default: <c>%LOCALAPPDATA%\VSMCP\logs</c>.</summary>
    public string? LogDirectory { get; set; }

    [JsonIgnore] public string? LoadedFromPath { get; set; }
    [JsonIgnore] public string? LoadError { get; set; }

    public static string DefaultRootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSMCP");

    public static string DefaultConfigPath => Path.Combine(DefaultRootDir, "config.json");

    public string ResolveLogDirectory() =>
        string.IsNullOrWhiteSpace(LogDirectory)
            ? Path.Combine(DefaultRootDir, "logs")
            : Path.GetFullPath(LogDirectory!);

    public static VsmcpConfig Load() => Load(DefaultConfigPath);

    public static VsmcpConfig Load(string path)
    {
        var config = new VsmcpConfig();
        if (!File.Exists(path)) return config;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<VsmcpConfig>(json, JsonOptions);
            if (parsed is not null)
            {
                config = parsed;
                config.LoadedFromPath = path;
            }
        }
        catch (Exception ex)
        {
            config.LoadError = $"Failed to parse {path}: {ex.Message}";
        }
        return config;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
