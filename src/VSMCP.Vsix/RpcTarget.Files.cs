using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<FileReadResult> FileReadAsync(string path, FileRange? range, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        string content;
        bool openInEditor = buffer is not null;
        bool hasUnsavedChanges = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            if (range is not null)
            {
                var span = VsHelpers.ToSpan(snapshot, range);
                content = snapshot.GetText(span);
            }
            else
            {
                content = snapshot.GetText();
            }

            try
            {
                if (buffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out var doc))
                    hasUnsavedChanges = doc.IsDirty;
            }
            catch { }
        }
        else
        {
            if (!File.Exists(path))
                throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

            var full = File.ReadAllText(path);
            if (range is not null)
            {
                var (start, length) = VsHelpers.ToOffsets(full, range);
                content = full.Substring(start, length);
            }
            else
            {
                content = full;
            }
        }

        return new FileReadResult
        {
            Path = path,
            Content = content,
            OpenInEditor = openInEditor,
            HasUnsavedChanges = hasUnsavedChanges,
        };
    }

    public async Task<FileWriteResult> FileWriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        content ??= string.Empty;
        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        bool wentThroughEditor = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            using var edit = buffer.CreateEdit();
            edit.Replace(new Span(0, snapshot.Length), content);
            edit.Apply();
            wentThroughEditor = true;
        }
        else
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = Encoding.UTF8.GetByteCount(content),
            WentThroughEditor = wentThroughEditor,
        };
    }

    public async Task<FileWriteResult> FileReplaceRangeAsync(string path, FileRange range, string text, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");
        if (range is null)
            throw new VsmcpException(ErrorCodes.NotFound, "Range is required.");

        text ??= string.Empty;
        var buffer = VsHelpers.TryGetOpenTextBuffer(_package, path);
        bool wentThroughEditor = false;

        if (buffer is not null)
        {
            var snapshot = buffer.CurrentSnapshot;
            var span = VsHelpers.ToSpan(snapshot, range);
            using var edit = buffer.CreateEdit();
            edit.Replace(span, text);
            edit.Apply();
            wentThroughEditor = true;
        }
        else
        {
            if (!File.Exists(path))
                throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

            var full = File.ReadAllText(path);
            var (start, length) = VsHelpers.ToOffsets(full, range);
            var sb = new StringBuilder(full.Length + text.Length);
            sb.Append(full, 0, start);
            sb.Append(text);
            sb.Append(full, start + length, full.Length - (start + length));
            File.WriteAllText(path, sb.ToString());
        }

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = Encoding.UTF8.GetByteCount(text),
            WentThroughEditor = wentThroughEditor,
        };
    }

    public async Task EditorOpenAsync(string path, int? line, int? column, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");
        if (!File.Exists(path))
            throw new VsmcpException(ErrorCodes.NotFound, $"File not found: {path}");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var window = dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindPrimary);
        if (line is not null && window?.Document?.Selection is EnvDTE.TextSelection sel)
        {
            sel.MoveToLineAndOffset(Math.Max(1, line.Value), Math.Max(1, column ?? 1));
        }
    }

    public async Task EditorSaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            throw new VsmcpException(ErrorCodes.NotFound, "Path is required.");

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        foreach (EnvDTE.Document doc in dte.Documents)
        {
            if (doc is null) continue;
            if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase))
            {
                doc.Save();
                return;
            }
        }
        throw new VsmcpException(ErrorCodes.NotFound, $"No open document matching '{path}'.");
    }

    public async Task EditorSaveAllAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        dte.Documents.SaveAll();
    }
}
