using VSMCP.Shared;

namespace VSMCP.Core;

/// <summary>
/// Global ceiling for side-effecting debug operations (#133). The bridge config exposes a
/// single <c>allowSideEffects</c> switch; an individual tool's per-call <c>allowSideEffects</c>
/// flag only takes effect when the global switch is also on. Default config is <c>true</c>, so
/// behavior is unchanged unless an operator deliberately locks the instance down.
/// </summary>
public static class SideEffectPolicy
{
    /// <summary>
    /// Resolve the effective allow flag. Returns <c>requested &amp;&amp; globallyAllowed</c>, but
    /// throws <see cref="VsmcpException"/> (<see cref="ErrorCodes.WrongState"/>) when a side
    /// effect is requested while the global gate is off, so the caller gets a clear reason
    /// rather than a silent no-op.
    /// </summary>
    public static bool Enforce(bool requested, bool globallyAllowed, string operation)
    {
        if (requested && !globallyAllowed)
            throw new VsmcpException(ErrorCodes.WrongState,
                $"{operation} needs side effects, but they are disabled globally. " +
                "Set \"allowSideEffects\": true in %LOCALAPPDATA%\\VSMCP\\config.json to allow them.");
        return requested && globallyAllowed;
    }
}
