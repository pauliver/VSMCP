using System;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.CppAnalyzer;

/// <summary>
/// Implements <see cref="IVsmcpCppRpc"/>. Phase F-1 ships only Ping; the libclang-backed
/// methods come in F-2 and F-3 once <see cref="CppAnalysis"/> is wired up.
/// </summary>
internal sealed class CppAnalysisService : IVsmcpCppRpc, IDisposable
{
    private readonly CppAnalysis _analysis = new();

    public Task<PingResult> PingAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PingResult
        {
            Message = "pong",
            ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });

    public Task<CppDiagnosticsResult> DiagnosticsAsync(string file, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
        => Task.FromResult(_analysis.Diagnostics(file, extraIncludes, extraDefines, cancellationToken));

    public Task<CppLocationListResult> FindReferencesAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
        => Task.FromResult(_analysis.FindReferences(file, line, column, extraIncludes, extraDefines, cancellationToken));

    public Task<CppQuickInfoResult> QuickInfoAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
        => Task.FromResult(_analysis.QuickInfo(file, line, column, extraIncludes, extraDefines, cancellationToken));

    public Task<CppLocationResult> GotoDefinitionAsync(string file, int line, int column, string[]? extraIncludes, string[]? extraDefines, CancellationToken cancellationToken = default)
        => Task.FromResult(_analysis.GotoDefinition(file, line, column, extraIncludes, extraDefines, cancellationToken));

    public Task InvalidateAsync(string file, CancellationToken cancellationToken = default)
    {
        _analysis.Invalidate(file);
        return Task.CompletedTask;
    }

    public void Dispose() => _analysis.Dispose();
}
