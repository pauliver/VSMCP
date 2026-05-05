using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix
{
    internal sealed partial class RpcTarget
    {
        public async Task<BuildSummaryResult> BuildSummaryAsync(string buildId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(buildId))
                throw new VsmcpException(ErrorCodes.NotFound, "buildId is required.");
            var status = await BuildStatusAsync(buildId, cancellationToken).ConfigureAwait(false);
            var errors = await BuildErrorsAsync(buildId, cancellationToken).ConfigureAwait(false);
            var warnings = await BuildWarningsAsync(buildId, cancellationToken).ConfigureAwait(false);
            var byProject = new Dictionary<string, BuildProjectSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in errors)
            {
                var key = e.Project ?? "<solution>";
                if (!byProject.TryGetValue(key, out var p))
                {
                    p = new BuildProjectSummary
                    {
                        Name = key,
                        Status = "Failed"
                    };
                    byProject[key] = p;
                }

                p.Errors++;
                if (p.FirstError is null)
                {
                    p.FirstError = CtxHelpers.Compact(e, CodeDiagnosticSeverity.Error);
                    p.FirstErrorFile = e.File;
                }
            }

            foreach (var w in warnings)
            {
                var key = w.Project ?? "<solution>";
                if (!byProject.TryGetValue(key, out var p))
                {
                    p = new BuildProjectSummary
                    {
                        Name = key,
                        Status = "Succeeded"
                    };
                    byProject[key] = p;
                }

                p.Warnings++;
            }

            // Add succeeded projects that had no diagnostics.
            await _jtf.SwitchToMainThreadAsync(cancellationToken);
            if (await _package.GetServiceAsync(typeof(EnvDTE.DTE))is EnvDTE80.DTE2 dte)
            {
                foreach (var proj in VsHelpers.EnumerateProjects(dte.Solution))
                {
                    var name = proj.Name ?? proj.UniqueName ?? "";
                    if (!byProject.ContainsKey(name))
                        byProject[name] = new BuildProjectSummary
                        {
                            Name = name,
                            Status = "Succeeded"
                        };
                }
            }

            var outcome = status.State.ToString();
            _lastBuildAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastBuildOutcome = outcome;
            long duration = 0;
            if (status.EndedAtMs is long ended && ended > 0 && status.StartedAtMs > 0)
                duration = ended - status.StartedAtMs;
            return new BuildSummaryResult
            {
                BuildId = buildId,
                Outcome = outcome,
                Projects = byProject.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList(),
                TotalErrors = status.ErrorCount,
                TotalWarnings = status.WarningCount,
                DurationMs = duration,
            };
        }
public async Task<GroupedDiagnosticsResult> BuildErrorsGroupedAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var errors = await BuildErrorsAsync(buildId, cancellationToken).ConfigureAwait(false);
        var warnings = await BuildWarningsAsync(buildId, cancellationToken).ConfigureAwait(false);
        return CtxHelpers.GroupBuildDiagnostics(errors, warnings);
    }
    }
}