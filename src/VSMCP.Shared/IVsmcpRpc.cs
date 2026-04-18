using System.Threading;
using System.Threading.Tasks;

namespace VSMCP.Shared;

/// <summary>
/// JSON-RPC contract implemented by the VSIX and called by VSMCP.Server.
/// Method names are stable — any breaking change bumps <see cref="ProtocolVersion"/>.
/// </summary>
public interface IVsmcpRpc
{
    Task<HandshakeResult> HandshakeAsync(int clientMajor, int clientMinor, CancellationToken cancellationToken = default);

    Task<PingResult> PingAsync(CancellationToken cancellationToken = default);

    Task<VsStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
