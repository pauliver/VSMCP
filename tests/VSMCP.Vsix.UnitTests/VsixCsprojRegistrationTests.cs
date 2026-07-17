using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// The VSIX csproj is non-SDK: a source file NOT listed in a &lt;Compile Include&gt; silently
/// doesn't compile into the extension — an entire RpcTarget partial can vanish from the
/// deployed VSIX with no build error. This locks every top-level .cs in src/VSMCP.Vsix to a
/// csproj entry.
/// </summary>
public class VsixCsprojRegistrationTests
{
    [Fact]
    public void Every_top_level_source_file_is_registered_in_the_csproj()
    {
        var vsixDir = LocateVsixDir();
        var csproj = File.ReadAllText(Path.Combine(vsixDir, "VSMCP.Vsix.csproj"));

        var missing = Directory.GetFiles(vsixDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => csproj.IndexOf($"Include=\"{name}\"", StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();

        Assert.True(missing.Count == 0,
            "Source files with no <Compile Include> entry in VSMCP.Vsix.csproj (they will NOT be compiled into the VSIX): "
            + string.Join(", ", missing));
    }

    private static string LocateVsixDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "VSMCP.Vsix");
            if (File.Exists(Path.Combine(candidate, "VSMCP.Vsix.csproj"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate src/VSMCP.Vsix relative to test binaries.");
    }
}
