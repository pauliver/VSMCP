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

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // Per-target session scope (#82). Lives for the connection's lifetime.
    private SessionScope? _session;
    private long _lastBuildAtMs;
    private string? _lastBuildOutcome;

    // -------- Phase 1 --------

    
    
    
    
    // -------- Phase 2 --------

    
    
    
    
    // -------- Phase 3 --------

    
    
    
    

    
    
    // -------- Phase 4 --------

    
    
    
    }
