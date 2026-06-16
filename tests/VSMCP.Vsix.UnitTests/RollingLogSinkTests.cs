using System;
using System.IO;
using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers the extracted log sink (#130).</summary>
public class RollingLogSinkTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"vsmcp-sink-{Guid.NewGuid():N}.log");

    [Fact]
    public void Append_writes_each_line()
    {
        var path = TempPath();
        try
        {
            var sink = new RollingLogSink(path);
            sink.Append("alpha");
            sink.Append("beta");
            var text = File.ReadAllText(path);
            Assert.Contains("alpha", text);
            Assert.Contains("beta", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Truncates_existing_file_on_construct()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "stale content");
            var sink = new RollingLogSink(path, truncate: true);
            sink.Append("fresh");
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("stale", text);
            Assert.Contains("fresh", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Null_path_is_a_silent_noop()
    {
        var sink = new RollingLogSink(null);
        sink.Append("ignored");          // must not throw
        Assert.Null(sink.FilePath);
    }
}
