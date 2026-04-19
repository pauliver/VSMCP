using System;
using System.Collections.Generic;
using System.Threading;

namespace VSMCP.Vsix;

/// <summary>
/// Observable state of the pipe host: how many clients are attached, when activity
/// last happened, and a bounded ring of recent RPC calls. Purely in-memory; one
/// instance per <see cref="VSMCPPackage"/>. Thread-safe for the writer path
/// (<see cref="PipeHost"/>) and for snapshot reads from the UI.
/// </summary>
internal sealed class HostActivity
{
    private const int RecentCapacity = 50;
    private readonly object _gate = new();
    private readonly LinkedList<RpcEntry> _recent = new();
    private int _clientCount;
    private long _rpcCount;
    private long _rpcErrorCount;
    private DateTime _lastActivityUtc;
    private DateTime _startedUtc = DateTime.UtcNow;
    private string? _lastError;

    public string PipeName { get; set; } = "";

    public event EventHandler? Changed;

    public void OnConnected()
    {
        lock (_gate)
        {
            _clientCount++;
            _lastActivityUtc = DateTime.UtcNow;
        }
        Raise();
    }

    public void OnDisconnected()
    {
        lock (_gate)
        {
            if (_clientCount > 0) _clientCount--;
            _lastActivityUtc = DateTime.UtcNow;
        }
        Raise();
    }

    public void OnRpcCompleted(string method, double elapsedMs, string? error)
    {
        lock (_gate)
        {
            _rpcCount++;
            _lastActivityUtc = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(error))
            {
                _rpcErrorCount++;
                _lastError = $"{method}: {error}";
            }
            _recent.AddFirst(new RpcEntry(DateTime.UtcNow, method, elapsedMs, error));
            while (_recent.Count > RecentCapacity) _recent.RemoveLast();
        }
        Raise();
    }

    public Snapshot GetSnapshot()
    {
        lock (_gate)
        {
            var entries = new RpcEntry[_recent.Count];
            int i = 0;
            foreach (var e in _recent) entries[i++] = e;
            return new Snapshot(
                PipeName,
                _clientCount,
                _rpcCount,
                _rpcErrorCount,
                _lastActivityUtc,
                _startedUtc,
                _lastError,
                entries);
        }
    }

    private void Raise()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
    }

    internal readonly struct RpcEntry
    {
        public RpcEntry(DateTime whenUtc, string method, double elapsedMs, string? error)
        {
            WhenUtc = whenUtc;
            Method = method;
            ElapsedMs = elapsedMs;
            Error = error;
        }

        public DateTime WhenUtc { get; }
        public string Method { get; }
        public double ElapsedMs { get; }
        public string? Error { get; }
    }

    internal sealed class Snapshot
    {
        public Snapshot(string pipeName, int clientCount, long rpcCount, long rpcErrorCount,
                        DateTime lastActivityUtc, DateTime startedUtc, string? lastError,
                        IReadOnlyList<RpcEntry> recent)
        {
            PipeName = pipeName;
            ClientCount = clientCount;
            RpcCount = rpcCount;
            RpcErrorCount = rpcErrorCount;
            LastActivityUtc = lastActivityUtc;
            StartedUtc = startedUtc;
            LastError = lastError;
            Recent = recent;
        }

        public string PipeName { get; }
        public int ClientCount { get; }
        public long RpcCount { get; }
        public long RpcErrorCount { get; }
        public DateTime LastActivityUtc { get; }
        public DateTime StartedUtc { get; }
        public string? LastError { get; }
        public IReadOnlyList<RpcEntry> Recent { get; }
    }
}
