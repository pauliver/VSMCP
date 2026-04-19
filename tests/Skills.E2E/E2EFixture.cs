using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Server;
using VSMCP.Shared;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// Shared setup for all skill e2e tests. Skipped unless <c>VSMCP_E2E=1</c> is set
/// in the environment — these tests require a running Visual Studio 2022
/// instance with the VSMCP VSIX loaded and take minutes to complete each.
///
/// Bootstraps a <see cref="VsConnection"/> + <see cref="VsmcpTools"/> so tests
/// call the exact same code paths that the MCP server would.
/// </summary>
public sealed class E2EFixture : IAsyncDisposable
{
    public const string EnableEnvVar = "VSMCP_E2E";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar), "true", StringComparison.OrdinalIgnoreCase);

    public static string SkipReason =>
        $"E2E tests are opt-in. Set {EnableEnvVar}=1 and make sure VS 2022 with the VSMCP extension is running.";

    public VsConnection Connection { get; }
    public VsmcpTools Tools { get; }

    /// <summary>Absolute path to <c>tests/Skills/</c> regardless of test working directory.</summary>
    public string FixturesRoot { get; }

    public E2EFixture()
    {
        Connection = new VsConnection();
        var config = VsmcpConfig.Load();
        Tools = new VsmcpTools(
            Connection,
            new ProfilerHost(),
            new CountersSubscriptionHost(),
            new TraceHost(),
            config);

        FixturesRoot = LocateFixturesRoot();
    }

    /// <summary>Connects to the single running VS instance, or throws if none/many.</summary>
    public async Task<IVsmcpRpc> ConnectAsync(CancellationToken ct = default)
        => await Connection.GetOrConnectAsync(ct).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync().ConfigureAwait(false);
    }

    private static string LocateFixturesRoot()
    {
        // Walk up from test bin folder until we see tests/Skills.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "tests", "Skills");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate tests/Skills/ relative to test binaries.");
    }
}
