using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// Force all e2e tests into a single collection — VS is not parallel-safe and
/// a single fixture instance connects once.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<E2EFixture>
{
    public const string Name = "VSMCP E2E (sequential)";
}
