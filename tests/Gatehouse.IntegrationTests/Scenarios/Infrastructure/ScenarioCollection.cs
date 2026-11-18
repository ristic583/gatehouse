namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

#pragma warning disable CA1711 // xUnit collection definitions conventionally end in "Collection".
/// <summary>
/// Physical-access scenarios share one AppHost (fixed ports, shared data volume): started once for the collection and never run
/// alongside another AppHost test class.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScenarioCollection : ICollectionFixture<ScenarioAppHostFixture>
{
    public const string Name = "Scenarios";
}
#pragma warning restore CA1711
