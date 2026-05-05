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
        public async Task<GroupedDiagnosticsResult> CodeDiagnosticsGroupedAsync(string? file, int maxResults, CancellationToken cancellationToken = default)
        {
            var raw = await CodeDiagnosticsAsync(file, maxResults <= 0 ? 1000 : maxResults, cancellationToken).ConfigureAwait(false);
            var grouped = CtxHelpers.GroupDiagnostics(raw.Diagnostics);
            grouped.Truncated = raw.Truncated;
            return grouped;
        }
    }
}