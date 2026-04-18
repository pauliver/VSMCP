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
    public bool AllowSideEffects { get; set; } = false;
    public bool AllowDbgEng { get; set; } = false;
    public int DefaultTimeoutMs { get; set; } = 30_000;
    public bool FileLoggingEnabled { get; set; } = false;

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
