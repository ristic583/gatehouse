using System.Net;
using System.Text.Json.Nodes;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>
/// One Keycloak organization per tenant: <c>name</c> = <c>alias</c> = tenant id, display name in <c>description</c>, the tenant's
/// verified email domain, and every user carrying the tenant attribute as a member.
/// </summary>
internal sealed class KeycloakOrganizations(KeycloakAdminClient client)
{
    public async Task<Result> EnsureAsync(TenantId tenantId, string displayName, string emailDomain, CancellationToken ct)
    {
        var domain = emailDomain.Trim().ToLowerInvariant();
        var org = await client.FindOrganizationAsync(tenantId, ct) ?? await client.FindOrganizationByDomainAsync(tenantId, domain, ct);
        if (org is null)
        {
            return await CreateAsync(tenantId, displayName, domain, ct);
        }

        var domains = org["domains"]?.AsArray() ?? [];
        var domainVerified = domains.Any(d =>
            string.Equals((string?)d?["name"], domain, StringComparison.OrdinalIgnoreCase) && (bool?)d?["verified"] == true);
        if (domainVerified && (string?)org["description"] == displayName && (string?)org["name"] == tenantId.Value)
        {
            return Result.Success();
        }

        // name = alias makes the organization findable by tenant id (realm-imported demo organizations carry a display name).
        org["name"] = tenantId.Value;
        org["description"] = displayName;
        org["domains"] = new JsonArray(new JsonObject { ["name"] = domain, ["verified"] = true });
        using var update = await client.SendAsync(HttpMethod.Put, $"organizations/{(string?)org["id"]}", org, ct);
        if (update.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            return DomainTaken(domain);
        }

        KeycloakAdminClient.Ensure(update, "organization.update");
        return Result.Success();
    }

    public async Task<Result> SetEnabledAsync(TenantId tenantId, bool enabled, CancellationToken ct)
    {
        var org = await client.FindOrganizationAsync(tenantId, ct);
        if (org is null)
        {
            return NoOrganization();
        }

        org["enabled"] = enabled;
        using var response = await client.SendAsync(HttpMethod.Put, $"organizations/{(string?)org["id"]}", org, ct);
        KeycloakAdminClient.Ensure(response, "organization.update");
        return Result.Success();
    }

    public async Task<Result> EnsureMembersAsync(TenantId tenantId, CancellationToken ct)
    {
        var org = await client.FindOrganizationAsync(tenantId, ct);
        if (org is null)
        {
            return NoOrganization();
        }

        const string attribute = KeycloakAdminClient.TenantAttribute;
        var query = Uri.EscapeDataString($"{attribute}:{tenantId.Value}");
        var users = await client.GetJsonAsync($"users?q={query}&max=1000&briefRepresentation=false", "user.find", ct);
        foreach (var user in users?.AsArray() ?? [])
        {
            if (user?["attributes"]?[attribute]?.AsArray().Any(v => (string?)v == tenantId.Value) != true)
            {
                continue;
            }

            await client.AddOrganizationMemberAsync((string?)org["id"], (string?)user["id"], ct);
        }

        return Result.Success();
    }

    private async Task<Result> CreateAsync(TenantId tenantId, string displayName, string domain, CancellationToken ct)
    {
        var create = new JsonObject
        {
            ["name"] = tenantId.Value,
            ["alias"] = tenantId.Value,
            ["description"] = displayName,
            ["enabled"] = true,
            ["domains"] = new JsonArray(new JsonObject { ["name"] = domain, ["verified"] = true }),
        };
        using var response = await client.SendAsync(HttpMethod.Post, "organizations", create, ct);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            // Keycloak 26.6 answers 400 for a domain owned by another organization (verified); our alias/name are
            // server-generated and unique, so a refused create of a still-missing organization is the domain.
            return await client.FindOrganizationAsync(tenantId, ct) is null ? DomainTaken(domain) : Result.Success();
        }

        KeycloakAdminClient.Ensure(response, "organization.create");
        return Result.Success();
    }

    private static Error NoOrganization() => Error.NotFound("tenant.not_found", "The tenant has no identity organization.");

    private static Error DomainTaken(string domain) =>
        Error.Conflict("tenant.email_domain_taken", $"The email domain {domain} is already owned by another organization.");
}
