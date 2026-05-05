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
        public async Task<TestSummaryResult> TestRunSummaryAsync(string? filter, string? projectId, string? configuration, string mode, CancellationToken cancellationToken = default)
        {
            var full = await TestRunAsync(filter, projectId, configuration, cancellationToken).ConfigureAwait(false);
            mode = (mode ?? "summary").ToLowerInvariant();
            var summary = new TestSummaryResult
            {
                RunId = full.RunId,
                Passed = full.Passed,
                Failed = full.Failed,
                Skipped = full.Skipped,
                DurationMs = full.Results.Sum(r => r.DurationMs),
            };
            if (mode == "full")
            {
                summary.Failures = full.Results.Where(r => r.Outcome == TestOutcome.Failed).ToList();
                summary.OutputTail = full.Output;
                return summary;
            }

            var failures = full.Results.Where(r => r.Outcome == TestOutcome.Failed).ToList();
            summary.Failures = mode == "summary" ? failures.Take(5).ToList() : failures;
            if (mode == "summary" && failures.Count > 5)
            {
                summary.OutputTail = $"… and {failures.Count - 5} more failures. Call with mode='failures' to see all.";
            }
            else if (mode == "failures")
            {
                // Tail of vstest output — last 20 lines.
                if (!string.IsNullOrEmpty(full.Output))
                {
                    var lines = full.Output!.Split('\n');
                    var tail = lines.Skip(Math.Max(0, lines.Length - 20));
                    summary.OutputTail = string.Join("\n", tail);
                }
            }

            return summary;
        }
    }
}