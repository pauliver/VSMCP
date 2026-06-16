using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VSMCP.Vsix;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Covers the pure launchSettings.json merge that makes debug.launch's args/cwd/env overrides
/// work for SDK-style projects (#146). The live E2E only exercised the create-from-nothing+args
/// path; these pin the merge/selection edge cases.
/// </summary>
public class LaunchSettingsMergerTests
{
    private static JObject Profiles(string json) => (JObject)JObject.Parse(json)["profiles"]!;

    [Fact]
    public void NoExistingFile_CreatesProjectProfileWithArgs()
    {
        var json = LaunchSettingsMerger.Merge(null, "MyApp", null, "ping --v", null, null);
        var profiles = Profiles(json);

        var profile = (JObject)profiles["MyApp"]!;
        Assert.Equal("Project", (string?)profile["commandName"]);
        Assert.Equal("ping --v", (string?)profile["commandLineArgs"]);
    }

    [Fact]
    public void ExistingProfile_UpdatesArgsAndPreservesOtherKeysAndProfiles()
    {
        var existing = @"{
          ""profiles"": {
            ""MyApp"": { ""commandName"": ""Project"", ""commandLineArgs"": ""old"", ""nativeDebugging"": true },
            ""IIS Express"": { ""commandName"": ""IISExpress"" }
          }
        }";

        var json = LaunchSettingsMerger.Merge(existing, "MyApp", null, "new", null, null);
        var profiles = Profiles(json);

        var profile = (JObject)profiles["MyApp"]!;
        Assert.Equal("new", (string?)profile["commandLineArgs"]);
        Assert.Equal(true, (bool?)profile["nativeDebugging"]);          // preserved
        Assert.NotNull(profiles["IIS Express"]);                        // unrelated profile preserved
    }

    [Fact]
    public void ActiveProfile_IsPreferredOverProjectNamedProfile()
    {
        var existing = @"{
          ""profiles"": {
            ""Alt"":   { ""commandName"": ""Project"" },
            ""MyApp"": { ""commandName"": ""Project"" }
          }
        }";

        var json = LaunchSettingsMerger.Merge(existing, "MyApp", "Alt", "x", null, null);
        var profiles = Profiles(json);

        Assert.Equal("x", (string?)profiles["Alt"]!["commandLineArgs"]);
        Assert.Null(profiles["MyApp"]!["commandLineArgs"]);             // project-named left alone
    }

    [Fact]
    public void NoNameOrActiveMatch_FallsBackToFirstProjectProfile()
    {
        var existing = @"{ ""profiles"": { ""Custom"": { ""commandName"": ""Project"" } } }";

        var json = LaunchSettingsMerger.Merge(existing, "MyApp", null, "y", null, null);
        var profiles = Profiles(json);

        Assert.Equal("y", (string?)profiles["Custom"]!["commandLineArgs"]);
    }

    [Fact]
    public void NullArgs_DoesNotOverwriteExistingArgs()
    {
        var existing = @"{ ""profiles"": { ""MyApp"": { ""commandName"": ""Project"", ""commandLineArgs"": ""keep"" } } }";

        var json = LaunchSettingsMerger.Merge(existing, "MyApp", null, null, "C:\\work", null);
        var profile = (JObject)Profiles(json)["MyApp"]!;

        Assert.Equal("keep", (string?)profile["commandLineArgs"]);      // untouched
        Assert.Equal("C:\\work", (string?)profile["workingDirectory"]); // cwd applied
    }

    [Fact]
    public void Env_IsSerializedAsObject()
    {
        var env = new Dictionary<string, string> { ["FOO"] = "1", ["BAR"] = "two" };
        var json = LaunchSettingsMerger.Merge(null, "MyApp", null, null, null, env);

        var envObj = (JObject)Profiles(json)["MyApp"]!["environmentVariables"]!;
        Assert.Equal("1", (string?)envObj["FOO"]);
        Assert.Equal("two", (string?)envObj["BAR"]);
    }
}
