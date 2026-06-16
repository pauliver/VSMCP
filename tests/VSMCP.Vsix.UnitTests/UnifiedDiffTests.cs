using System.Linq;
using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers the unified-diff parser + applier (#137).</summary>
public class UnifiedDiffTests
{
    private const string Original = "line1\nline2\nline3\nline4\n";

    [Fact]
    public void Parse_reads_paths_and_hunk_range()
    {
        var diff = "--- a/foo.cs\n+++ b/foo.cs\n@@ -2,2 +2,3 @@\n line2\n+inserted\n line3\n";
        var patches = UnifiedDiff.Parse(diff);

        var p = Assert.Single(patches);
        Assert.Equal("foo.cs", p.OldPath);
        Assert.Equal("foo.cs", p.NewPath);
        var h = Assert.Single(p.Hunks);
        Assert.Equal(2, h.OldStart);
        Assert.Equal(2, h.OldCount);
        Assert.Equal(3, h.NewCount);
    }

    [Fact]
    public void Apply_inserts_line()
    {
        var diff = "--- a/f\n+++ b/f\n@@ -2,2 +2,3 @@\n line2\n+inserted\n line3\n";
        var patch = UnifiedDiff.Parse(diff).Single();
        var r = UnifiedDiff.Apply(Original, patch);

        Assert.True(r.Success);
        Assert.Equal("line1\nline2\ninserted\nline3\nline4\n", r.NewText);
        Assert.Equal(1, r.HunksApplied);
    }

    [Fact]
    public void Apply_deletes_line()
    {
        var diff = "--- a/f\n+++ b/f\n@@ -2,2 +2,1 @@\n line2\n-line3\n";
        var patch = UnifiedDiff.Parse(diff).Single();
        var r = UnifiedDiff.Apply(Original, patch);

        Assert.True(r.Success);
        Assert.Equal("line1\nline2\nline4\n", r.NewText);
    }

    [Fact]
    public void Apply_replaces_line()
    {
        var diff = "--- a/f\n+++ b/f\n@@ -3,1 +3,1 @@\n-line3\n+LINE3\n";
        var patch = UnifiedDiff.Parse(diff).Single();
        var r = UnifiedDiff.Apply(Original, patch);

        Assert.True(r.Success);
        Assert.Equal("line1\nline2\nLINE3\nline4\n", r.NewText);
    }

    [Fact]
    public void Apply_tolerates_small_line_drift_via_fuzz()
    {
        // Hunk says line 2 but the real content is shifted down by one extra prefix line.
        var shifted = "header\nline1\nline2\nline3\nline4\n";
        var diff = "--- a/f\n+++ b/f\n@@ -2,2 +2,3 @@\n line2\n+inserted\n line3\n";
        var patch = UnifiedDiff.Parse(diff).Single();
        var r = UnifiedDiff.Apply(shifted, patch);

        Assert.True(r.Success);
        Assert.Contains("line2\ninserted\nline3", r.NewText);
    }

    [Fact]
    public void Apply_reports_failure_on_context_mismatch()
    {
        var diff = "--- a/f\n+++ b/f\n@@ -2,2 +2,3 @@\n totally different\n+x\n also wrong\n";
        var patch = UnifiedDiff.Parse(diff).Single();
        var r = UnifiedDiff.Apply(Original, patch);

        Assert.False(r.Success);
        Assert.Equal(1, r.HunksFailed);
        Assert.Null(r.NewText);
    }

    [Fact]
    public void Parse_handles_multiple_files()
    {
        var diff =
            "--- a/one.cs\n+++ b/one.cs\n@@ -1,1 +1,1 @@\n-a\n+A\n" +
            "--- a/two.cs\n+++ b/two.cs\n@@ -1,1 +1,1 @@\n-b\n+B\n";
        var patches = UnifiedDiff.Parse(diff);
        Assert.Equal(2, patches.Count);
        Assert.Equal("two.cs", patches[1].NewPath);
    }
}
