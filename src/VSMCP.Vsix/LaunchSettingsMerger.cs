using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VSMCP.Vsix;

/// <summary>
/// Pure launchSettings.json merge logic for <c>debug.launch</c> overrides (#146). SDK-style
/// projects ignore the legacy DTE StartArguments/StartWorkingDirectory/EnvironmentVariables
/// properties and read launch args from a launchSettings.json profile instead, so the override
/// has to be written there. Kept free of EnvDTE/file-system dependencies so it can be unit-tested.
/// </summary>
public static class LaunchSettingsMerger
{
    /// <summary>
    /// Merge launch overrides into launchSettings.json content and return the updated JSON.
    /// <paramref name="existingJson"/> may be null/empty (a fresh file is produced). The target
    /// profile is chosen as: <paramref name="activeProfile"/> if it exists, else one named
    /// <paramref name="projectName"/>, else the first <c>commandName=="Project"</c> profile, else
    /// a new profile named <paramref name="projectName"/>. Null overrides are left untouched so
    /// existing profile values and unrelated profiles are preserved.
    /// </summary>
    public static string Merge(
        string? existingJson,
        string projectName,
        string? activeProfile,
        string? args,
        string? cwd,
        IReadOnlyDictionary<string, string>? env)
    {
        JObject root = string.IsNullOrWhiteSpace(existingJson) ? new JObject() : JObject.Parse(existingJson!);

        if (root["profiles"] is not JObject profiles)
        {
            profiles = new JObject();
            root["profiles"] = profiles;
        }

        JObject? profile = null;
        if (!string.IsNullOrEmpty(activeProfile)) profile = profiles[activeProfile!] as JObject;
        if (profile is null) profile = profiles[projectName] as JObject;
        if (profile is null)
        {
            foreach (var p in profiles.Properties())
                if (p.Value is JObject o && (string?)o["commandName"] == "Project") { profile = o; break; }
        }
        if (profile is null)
        {
            profile = new JObject { ["commandName"] = "Project" };
            profiles[projectName] = profile;
        }

        if (args is not null) profile["commandLineArgs"] = args;
        if (cwd is not null) profile["workingDirectory"] = cwd;
        if (env is { Count: > 0 })
        {
            var envObj = new JObject();
            foreach (var kv in env) envObj[kv.Key] = kv.Value;
            profile["environmentVariables"] = envObj;
        }

        return root.ToString(Formatting.Indented);
    }
}
