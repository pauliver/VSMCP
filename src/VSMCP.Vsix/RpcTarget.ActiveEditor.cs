using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- Active editor surface --------

    public async Task<ActiveEditorInfo> EditorActiveAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            return new ActiveEditorInfo();

        var info = new ActiveEditorInfo();
        try
        {
            var doc = dte.ActiveDocument;
            if (doc is null) return info;

            info.File = doc.FullName;
            info.Language = doc.Language;
            info.IsDirty = !doc.Saved;

            if (doc.Selection is EnvDTE.TextSelection sel)
            {
                info.CursorLine = sel.ActivePoint.Line;
                info.CursorColumn = sel.ActivePoint.LineCharOffset;
            }
        }
        catch { }
        return info;
    }

    public async Task<EditorSelection?> EditorSelectionAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte) return null;
        try
        {
            var doc = dte.ActiveDocument;
            if (doc?.Selection is not EnvDTE.TextSelection sel) return null;

            var top = sel.TopPoint;
            var bot = sel.BottomPoint;
            return new EditorSelection
            {
                File = doc.FullName,
                StartLine = top.Line,
                StartColumn = top.LineCharOffset,
                EndLine = bot.Line,
                EndColumn = bot.LineCharOffset,
                Text = sel.Text ?? "",
            };
        }
        catch { return null; }
    }

    public async Task<CodePosition?> EditorCursorAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte) return null;
        try
        {
            var doc = dte.ActiveDocument;
            if (doc?.Selection is not EnvDTE.TextSelection sel) return null;
            return new CodePosition
            {
                File = doc.FullName,
                Line = sel.ActivePoint.Line,
                Column = sel.ActivePoint.LineCharOffset,
            };
        }
        catch { return null; }
    }

    public async Task<FileWriteResult> EditorInsertAtCursorAsync(
        string text, CancellationToken cancellationToken = default)
    {
        if (text is null) text = "";
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var doc = dte.ActiveDocument
            ?? throw new VsmcpException(ErrorCodes.WrongState, "No active document.");
        if (doc.Selection is not EnvDTE.TextSelection sel)
            throw new VsmcpException(ErrorCodes.WrongState, "Active document has no text selection.");

        var path = doc.FullName;
        sel.Insert(text, (int)EnvDTE.vsInsertFlags.vsInsertFlagsContainNewText);

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = System.Text.Encoding.UTF8.GetByteCount(text),
            WentThroughEditor = true,
        };
    }
}
