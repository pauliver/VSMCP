using System.Text.Json;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Server;

/// <summary>
/// Guards the JSON wire shape after the #129 de-dup: CodePosition replacing the position item
/// types, and ResultBase carrying Truncated, must serialize identically to before.
/// </summary>
public class DtoShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CodePosition_serializes_File_Line_Column()
    {
        var json = JsonSerializer.Serialize(new CodePosition { File = "a.cs", Line = 3, Column = 5 }, Web);
        Assert.Contains("\"file\":\"a.cs\"", json);
        Assert.Contains("\"line\":3", json);
        Assert.Contains("\"column\":5", json);
    }

    [Fact]
    public void ResultBase_derived_still_emits_Truncated()
    {
        var json = JsonSerializer.Serialize(new CppIndexResult { TotalIndexed = 2, Truncated = true }, Web);
        Assert.Contains("\"truncated\":true", json);
        Assert.Contains("\"totalIndexed\":2", json);
        Assert.Contains("\"entries\":", json);
    }

    [Fact]
    public void CompletionResult_truncated_is_inherited_not_lost()
    {
        var json = JsonSerializer.Serialize(new CompletionResult { Truncated = false }, Web);
        Assert.Contains("\"truncated\":false", json);
        Assert.Contains("\"items\":", json);
    }
}
