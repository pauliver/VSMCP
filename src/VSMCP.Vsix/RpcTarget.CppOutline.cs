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
        var result = CppOutlineParser.Parse(file);
        if (Follow.Enabled)
            await Follow.TouchAsync(file, 1, 1, isEdit: false, cancellationToken).ConfigureAwait(false);
        return result;
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

        // Follow Mode: open the file and scroll to the class declaration line if found.
        // Fall back to the first member's line when className is actually a namespace
        // (the user is asking "what's in this scope?"); finally to line 1.
        if (Follow.Enabled)
        {
            var scopeDecl = outline.Declarations.FirstOrDefault(d =>
                (d.Kind == "class" || d.Kind == "struct" || d.Kind == "union" || d.Kind == "namespace")
                && d.Name == className);
            int targetLine = scopeDecl?.Line ?? members.FirstOrDefault()?.Line ?? 1;
            await Follow.TouchAsync(file, targetLine, 1, isEdit: false, cancellationToken).ConfigureAwait(false);
        }

        return new CppMembersResult
        {
            File = file,
            ClassName = className,
            Members = members,
        };
    }
}
