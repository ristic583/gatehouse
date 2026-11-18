namespace Gatehouse.IntegrationTests.AppHost;

#pragma warning disable CA1711 // xUnit collection definitions conventionally end in "Collection".
/// <summary>AppHost tests use fixed host ports and a shared data volume: never run them alongside another AppHost test class.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppHostCollection
{
    public const string Name = "AppHost";
}
#pragma warning restore CA1711
