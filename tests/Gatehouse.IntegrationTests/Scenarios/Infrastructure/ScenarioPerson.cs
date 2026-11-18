namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>An employee created for one scenario, with one active card.</summary>
public sealed record ScenarioPerson(Guid PersonId, string Email, Guid CardId, string CardNumber);
