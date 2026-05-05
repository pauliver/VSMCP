using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<CppOutlineResult> CppOutlineAsync(string file, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        await Task.Yield();
        return CppOutlineParser.Parse(file);
    }

    public async Task<CppMembersResult> CppClassMembersAsync(string file, string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        await Task.Yield();

        var outline = CppOutlineParser.Parse(file);
        // Members of <className> = declarations whose Container ends with className OR whose
        // Container path's last segment equals className.
        var members = outline.Declarations
            .Where(d => d.Container is not null
                        && (d.Container == className
                            || d.Container.EndsWith("::" + className, System.StringComparison.Ordinal)))
            .ToList();

        return new CppMembersResult
        {
            File = file,
            ClassName = className,
            Members = members,
        };
    }
}
