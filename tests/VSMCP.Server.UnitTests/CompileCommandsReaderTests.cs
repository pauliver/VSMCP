using System;
using System.IO;
using VSMCP.Server;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Server;

/// <summary>Covers compile_commands.json reading + flag resolution (#135).</summary>
public class CompileCommandsReaderTests : IDisposable
{
    private readonly string _dir;
    public CompileCommandsReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vsmcp-cc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Command_form_resolves_includes_relative_to_directory()
    {
        var src = Path.Combine(_dir, "a.cpp");
        File.WriteAllText(src, "int main(){}");
        var cc = Write("compile_commands.json", $@"[
          {{ ""directory"": ""{_dir.Replace("\\", "/")}"", ""file"": ""a.cpp"",
             ""command"": ""clang++ -Iinclude -DNDEBUG -std=c++17 -c a.cpp"" }}
        ]");

        var r = CompileCommandsReader.Read(cc, src);

        Assert.True(r.Found);
        Assert.Equal("c++17", r.Std);
        Assert.Contains("NDEBUG", r.Defines);
        Assert.Contains(r.Includes, p => p.Replace('\\', '/').EndsWith("/include"));   // resolved to absolute
        Assert.True(Path.IsPathRooted(r.Includes[0]));
    }

    [Fact]
    public void Arguments_form_is_supported()
    {
        var src = Path.Combine(_dir, "b.cpp");
        var cc = Write("compile_commands.json", $@"[
          {{ ""directory"": ""{_dir.Replace("\\", "/")}"", ""file"": ""{src.Replace("\\", "/")}"",
             ""arguments"": [""clang++"", ""-I"", ""/abs/inc"", ""-DFOO=1"", ""-c"", ""b.cpp""] }}
        ]");

        var r = CompileCommandsReader.Read(cc, src);

        Assert.True(r.Found);
        Assert.Contains("/abs/inc", r.Includes);
        Assert.Contains("FOO=1", r.Defines);
    }

    [Fact]
    public void Unlisted_file_returns_not_found()
    {
        var cc = Write("compile_commands.json", "[]");
        var r = CompileCommandsReader.Read(cc, Path.Combine(_dir, "missing.cpp"));
        Assert.False(r.Found);
    }

    [Fact]
    public void Missing_file_throws_not_found()
    {
        var ex = Assert.Throws<VsmcpException>(() => CompileCommandsReader.Read(Path.Combine(_dir, "nope.json"), "x.cpp"));
        Assert.Equal(ErrorCodes.NotFound, ex.Code);
    }
}
