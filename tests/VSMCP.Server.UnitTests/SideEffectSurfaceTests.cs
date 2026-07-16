using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using VSMCP.Server;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Server;

/// <summary>
/// Symmetry guard for the side-effect gate. The <c>eval.expression_many</c> bypass shipped because
/// the batch variant diverged from the single-item tool. This asserts the set of MCP tools that
/// accept an <see cref="EvalOptions"/> (whose AllowSideEffects flag must be run through
/// <c>SideEffectPolicy.Enforce</c>) is exactly the eval pair — a third eval variant will fail this
/// test until it is added to the list, prompting the author to also wire the gate.
/// </summary>
public class SideEffectSurfaceTests
{
    [Fact]
    public void Eval_option_taking_tools_are_the_known_pair()
    {
        var names = typeof(VsmcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Where(TakesEvalOptions)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "eval.expression", "eval.expression_many" }, names);
    }

    private static bool TakesEvalOptions(MethodInfo m) => m.GetParameters().Any(p =>
    {
        var t = p.ParameterType;
        if (t == typeof(EvalOptions)) return true;
        // IReadOnlyList<EvalOptions> for the _many variant.
        return t.IsGenericType && t.GetGenericArguments().Contains(typeof(EvalOptions));
    });
}
