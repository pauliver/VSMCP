using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Batch tool surface. Each *_many variant takes a list and returns a
/// <see cref="BatchResult{T}"/> that preserves input order and captures
/// per-item errors without failing the whole call. Calls are run sequentially
/// because the VSIX serializes everything on the VS UI thread — parallelism
/// offers no speedup and complicates error ordering.
/// </summary>
public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "bp.set_many")]
    [Description("Set multiple breakpoints in one call. Per-item errors are returned in the result; one bad item does not fail the others.")]
    public async Task<BatchResult<BreakpointInfo>> BreakpointSetMany(
        [Description("Breakpoints to create. Each item is a full BreakpointSetOptions (same schema as bp.set).")] IReadOnlyList<BreakpointSetOptions> items,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(items, (opt, c) => proxy.BreakpointSetAsync(opt, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.remove_many")]
    [Description("Remove multiple breakpoints by id. Each item's Value on success is the removed id.")]
    public async Task<BatchResult<string>> BreakpointRemoveMany(
        [Description("Breakpoint ids to remove.")] IReadOnlyList<string> ids,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(ids, async (id, c) =>
        {
            await proxy.BreakpointRemoveAsync(id, c).ConfigureAwait(false);
            return id;
        }, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.enable_many")]
    [Description("Enable multiple breakpoints by id.")]
    public async Task<BatchResult<BreakpointInfo>> BreakpointEnableMany(
        [Description("Breakpoint ids to enable.")] IReadOnlyList<string> ids,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(ids, (id, c) => proxy.BreakpointEnableAsync(id, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.disable_many")]
    [Description("Disable multiple breakpoints by id (without removing them).")]
    public async Task<BatchResult<BreakpointInfo>> BreakpointDisableMany(
        [Description("Breakpoint ids to disable.")] IReadOnlyList<string> ids,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(ids, (id, c) => proxy.BreakpointDisableAsync(id, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "eval.expression_many")]
    [Description("Evaluate multiple expressions in one call (same EvalOptions shape as eval.expression). Useful for inspecting a set of locals in one round trip.")]
    public async Task<BatchResult<EvalResult>> EvalExpressionMany(
        [Description("Expressions to evaluate. Each item is a full EvalOptions.")] IReadOnlyList<EvalOptions> items,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(items, (opt, c) => proxy.EvalExpressionAsync(opt, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.read_many")]
    [Description("Read multiple files (or ranges) in one call. Each item specifies a Path and optional 1-based inclusive Range.")]
    public async Task<BatchResult<FileReadResult>> FileReadMany(
        [Description("Files to read; each item has a required Path and optional Range.")] IReadOnlyList<FileReadRequest> items,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(items, (req, c) => proxy.FileReadAsync(req.Path, req.Range, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "memory.read_many")]
    [Description("Read multiple memory ranges in one call. Each item specifies Address and Length; reads are capped at 64 KiB per item.")]
    public async Task<BatchResult<MemoryReadResult>> MemoryReadMany(
        [Description("Memory ranges to read; each item has an Address and a Length (1..65536).")] IReadOnlyList<MemoryReadRequest> items,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(items, (req, c) => proxy.MemoryReadAsync(req.Address, req.Length, c), ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "symbols.load_many")]
    [Description("Force-load symbols for multiple modules in one call.")]
    public async Task<BatchResult<SymbolStatusResult>> SymbolsLoadMany(
        [Description("Module ids from modules.list.")] IReadOnlyList<string> moduleIds,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await RunBatchAsync(moduleIds, (id, c) => proxy.SymbolsLoadAsync(id, c), ct).ConfigureAwait(false);
    }

    private static async Task<BatchResult<TOut>> RunBatchAsync<TIn, TOut>(
        IReadOnlyList<TIn> items,
        Func<TIn, CancellationToken, Task<TOut>> op,
        CancellationToken ct)
    {
        var result = new BatchResult<TOut> { Total = items.Count };
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = new BatchItemResult<TOut> { Index = i };
            try
            {
                entry.Value = await op(items[i], ct).ConfigureAwait(false);
                entry.Success = true;
                result.Succeeded++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                entry.Success = false;
                entry.Error = new BatchItemError
                {
                    Code = ex.GetType().Name,
                    Message = ex.Message,
                };
                result.Failed++;
            }
            result.Items.Add(entry);
        }
        return result;
    }
}
