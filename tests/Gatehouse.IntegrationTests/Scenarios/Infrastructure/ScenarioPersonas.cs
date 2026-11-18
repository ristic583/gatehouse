namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Nordlys demo users (tests/e2e/fixtures/demo-users.ts, infra/keycloak/demo-users.md) the scenarios act as.</summary>
public static class ScenarioPersonas
{
    /// <summary>Freja Dahl, tenant admin.</summary>
    public const string TenantAdmin = "freja.dahl@nordlys.test";

    /// <summary>Mikkel Holm, manager; requests on behalf of scenario employees.</summary>
    public const string Manager = "mikkel.holm@nordlys.test";

    /// <summary>Sofie Berg, area owner of the HQ Lab, Server Room and Vault.</summary>
    public const string AreaOwner = "sofie.berg@nordlys.test";

    /// <summary>Erik Møller, top manager and owner of the other areas (Aarhus Loading Dock included).</summary>
    public const string TopManager = "erik.moller@nordlys.test";

    /// <summary>Jonas Nielsen, security staff of HQ.</summary>
    public const string SecurityStaff = "jonas.nielsen@nordlys.test";

    public static IReadOnlyList<string> All { get; } = [TenantAdmin, Manager, AreaOwner, TopManager, SecurityStaff];

    /// <summary>Personas that may approve a step of a request someone else made.</summary>
    public static IReadOnlyList<string> Approvers { get; } = [AreaOwner, TopManager, Manager];
}
