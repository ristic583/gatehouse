namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A tenant provisioned from the platform panel for one scenario, with its signed-in first admin.</summary>
public sealed record ScenarioTenant(string TenantId, string EmailDomain, string AdminEmail, ScenarioClient Admin) : IDisposable
{
    public void Dispose() => Admin.Dispose();
}
