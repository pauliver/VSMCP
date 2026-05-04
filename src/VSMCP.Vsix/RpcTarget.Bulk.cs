using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- M14: Bulk Operations --------

    public async Task<BatchResult<FileReadResultItem>> FileReadManyAsync(
        IReadOnlyList<FileReadRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests is null || requests.Count == 0)
            return new BatchResult<FileReadResultItem>();

        var batch = new BatchResult<FileReadResultItem> { Total = requests.Count };
        for (int i = 0; i < requests.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var req = requests[i];
            var item = new BatchItemResult<FileReadResultItem> { Index = i };
            try
            {
                var r = await FileReadAsync(req.Path, req.Range, cancellationToken).ConfigureAwait(false);
                item.Value = new FileReadResultItem { Path = r.Path, Content = r.Content };
                item.Success = true;
                batch.Succeeded++;
            }
            catch (VsmcpException ex)
            {
                item.Error = new BatchItemError { Code = ex.Code, Message = ex.Message };
                batch.Failed++;
            }
            catch (Exception ex)
            {
                item.Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message };
                batch.Failed++;
            }
            batch.Items.Add(item);
        }
        return batch;
    }

    public async Task<BatchResult<FileWriteResultItem>> FileWriteManyAsync(
        IReadOnlyList<FileWriteEntry> entries, bool openInEditor, CancellationToken cancellationToken = default)
    {
        if (entries is null || entries.Count == 0)
            return new BatchResult<FileWriteResultItem>();

        var batch = new BatchResult<FileWriteResultItem> { Total = entries.Count };
        for (int i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            var item = new BatchItemResult<FileWriteResultItem> { Index = i };
            try
            {
                FileWriteResult r;
                if (entry.Range is not null && entry.Text is not null)
                    r = await FileReplaceRangeAsync(entry.Path, entry.Range, entry.Text, cancellationToken).ConfigureAwait(false);
                else
                    r = await FileWriteAsync(entry.Path, entry.Content ?? "", cancellationToken).ConfigureAwait(false);

                item.Value = new FileWriteResultItem
                {
                    Path = r.Path,
                    Bytes = r.BytesWritten,
                    OpenInEditor = r.WentThroughEditor,
                };
                item.Success = true;
                batch.Succeeded++;
            }
            catch (VsmcpException ex)
            {
                item.Error = new BatchItemError { Code = ex.Code, Message = ex.Message };
                batch.Failed++;
            }
            catch (Exception ex)
            {
                item.Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message };
                batch.Failed++;
            }
            batch.Items.Add(item);
        }
        return batch;
    }

    public async Task<ReplaceManyResult> SearchReplaceManyAsync(
        string pattern, string replacement, string? filePattern,
        int maxFiles, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new VsmcpException(ErrorCodes.NotFound, "pattern is required.");
        if (maxFiles <= 0) maxFiles = 1000;

        Regex rx;
        try { rx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
        catch (ArgumentException ex) { throw new VsmcpException(ErrorCodes.NotFound, $"Invalid regex: {ex.Message}"); }

        var files = await FileListAsync(null, null, filePattern, new[] { "file" }, maxFiles, cancellationToken);
        var result = new ReplaceManyResult();

        foreach (var f in files.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content;
            try { content = File.ReadAllText(f.Path); }
            catch { continue; }

            var matches = rx.Matches(content);
            if (matches.Count == 0) continue;

            result.Matched += matches.Count;
            var newContent = rx.Replace(content, replacement);
            if (!dryRun && newContent != content)
            {
                try { await FileWriteAsync(f.Path, newContent, cancellationToken).ConfigureAwait(false); }
                catch { continue; }
                result.Replaced += matches.Count;
            }
            else if (dryRun)
            {
                // Reported as matched but not replaced
            }

            result.Files.Add(new ReplaceManyFileResult
            {
                Path = f.Path,
                Replacements = matches.Count,
            });
        }
        return result;
    }

    public async Task<BatchResult<CodeBatchResult>> CodeSymbolsManyAsync(
        IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0)
            return new BatchResult<CodeBatchResult>();

        var batch = new BatchResult<CodeBatchResult> { Total = files.Count };
        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new BatchItemResult<CodeBatchResult> { Index = i };
            try
            {
                var r = await CodeSymbolsAsync(files[i], cancellationToken).ConfigureAwait(false);
                item.Value = new CodeBatchResult
                {
                    File = r.File,
                    Symbols = r.Symbols,
                    Language = r.Language,
                };
                item.Success = true;
                batch.Succeeded++;
            }
            catch (VsmcpException ex)
            {
                item.Error = new BatchItemError { Code = ex.Code, Message = ex.Message };
                batch.Failed++;
            }
            catch (Exception ex)
            {
                item.Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message };
                batch.Failed++;
            }
            batch.Items.Add(item);
        }
        return batch;
    }

    public async Task<BatchResult<ReferencesResult>> CodeFindReferencesManyAsync(
        IReadOnlyList<CodePosition> positions, int maxResults, CancellationToken cancellationToken = default)
    {
        if (positions is null || positions.Count == 0)
            return new BatchResult<ReferencesResult>();

        var batch = new BatchResult<ReferencesResult> { Total = positions.Count };
        for (int i = 0; i < positions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new BatchItemResult<ReferencesResult> { Index = i };
            try
            {
                var r = await CodeFindReferencesAsync(positions[i], maxResults, cancellationToken).ConfigureAwait(false);
                item.Value = r;
                item.Success = true;
                batch.Succeeded++;
            }
            catch (VsmcpException ex)
            {
                item.Error = new BatchItemError { Code = ex.Code, Message = ex.Message };
                batch.Failed++;
            }
            catch (Exception ex)
            {
                item.Error = new BatchItemError { Code = ErrorCodes.InteropFault, Message = ex.Message };
                batch.Failed++;
            }
            batch.Items.Add(item);
        }
        return batch;
    }
}
