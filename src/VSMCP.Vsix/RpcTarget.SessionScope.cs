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
        public async Task<SessionScopeResult> SessionScopeAsync(IReadOnlyList<string>? symbols, string? project, string? folder, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            _session = new SessionScope
            {
                Symbols = symbols?.ToList() ?? new List<string>(),
                Project = project,
                Folder = folder,
            };
            return new SessionScopeResult
            {
                ScopeId = _session.Id,
                ResolvedSymbols = _session.Symbols,
                ResolvedProject = _session.Project,
                ResolvedFolder = _session.Folder,
                EstablishedAtMs = _session.EstablishedAtMs,
            };
        }
public async Task<SessionCurrentResult> SessionCurrentAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_session is null) return new SessionCurrentResult { Active = false };
        return new SessionCurrentResult
        {
            Symbols = _session.Symbols,
            Project = _session.Project,
            Folder = _session.Folder,
            EstablishedAtMs = _session.EstablishedAtMs,
            Active = true,
        };
    }
public async Task SessionClearAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _session = null;
    }
public async Task<IoContextResult> IoContextAsync(CancellationToken cancellationToken = default)
    {
        var editor = await EditorActiveAsync(cancellationToken).ConfigureAwait(false);
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        DebugInfo? debugger = null;
        try { debugger = await DebugStateAsync(cancellationToken).ConfigureAwait(false); } catch { }

        return new IoContextResult
        {
            Editor = editor,
            Solution = status,
            Debugger = debugger,
            LastBuildAtMs = _lastBuildAtMs == 0 ? null : _lastBuildAtMs,
            LastBuildOutcome = _lastBuildOutcome,
        };
    }
    }
}