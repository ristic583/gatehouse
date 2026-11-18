using System.Text.Json.Nodes;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>Tenant admin invitations: a tenant-bound user in the tenant's organization plus an <c>UPDATE_PASSWORD</c> email.</summary>
internal sealed class KeycloakTenantAdminInvites(KeycloakAdminClient client, KeycloakOptions options, ILogger logger)
{
    private const string TenantAttribute = KeycloakAdminClient.TenantAttribute;

    public async Task<Result> InviteAsync(TenantId tenantId, string email, string givenName, string familyName, CancellationToken ct)
    {
        var org = await client.FindOrganizationAsync(tenantId, ct)
            ?? throw new KeycloakAdminException("organization.find", 404);
        var normalized = email.Trim().ToLowerInvariant();
        var user = await client.FindUserAsync(normalized, ct);
        string userId;
        if (user is null)
        {
            userId = await CreateUserAsync(tenantId, normalized, givenName, familyName, ct);
        }
        else
        {
            // Never adopt a user of another tenant.
            if (OwningTenant(user) != tenantId.Value)
            {
                return Error.Failure(
                    KeycloakIdentityProvisioner.FailureCode, "The invited email belongs to an existing user of another tenant.");
            }

            userId = (string)user["id"]!;
        }

        await client.AddOrganizationMemberAsync((string?)org["id"], userId, ct);

        var actionsPath = KeycloakIdentityProvisioner.InviteActionsPath(options, userId);
        using var mail = await client.SendRawAsync(HttpMethod.Put, actionsPath, new JsonArray("UPDATE_PASSWORD"), ct);
        KeycloakAdminClient.Ensure(mail, "user.execute_actions_email");
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Tenant admin invitation sent for tenant {TenantId}", tenantId.Value);
        }

        return Result.Success();
    }

    /// <summary>True once the tenant's user has a password or a linked upstream identity.</summary>
    public async Task<bool> HasSignedInAsync(TenantId tenantId, string email, CancellationToken ct)
    {
        var user = await client.FindUserAsync(email.Trim().ToLowerInvariant(), ct);
        if (user is null || OwningTenant(user) != tenantId.Value)
        {
            return false;
        }

        var id = (string)user["id"]!;
        var credentials = await client.GetJsonAsync($"users/{id}/credentials", "user.credentials", ct);
        if (credentials?.AsArray().Any(c => (string?)c?["type"] == "password") == true)
        {
            return true;
        }

        var links = await client.GetJsonAsync($"users/{id}/federated-identity", "user.federated_identity", ct);
        return links?.AsArray().Count > 0;
    }

    private static string? OwningTenant(JsonNode user) =>
        user["attributes"]?[TenantAttribute]?.AsArray().Select(v => (string?)v).FirstOrDefault();

    private async Task<string> CreateUserAsync(TenantId tenantId, string email, string givenName, string familyName, CancellationToken ct)
    {
        var create = new JsonObject
        {
            ["username"] = email,
            ["email"] = email,
            ["firstName"] = givenName,
            ["lastName"] = familyName,
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["attributes"] = new JsonObject { [TenantAttribute] = new JsonArray(tenantId.Value) },
        };
        using var response = await client.SendAsync(HttpMethod.Post, "users", create, ct);
        KeycloakAdminClient.Ensure(response, "user.create");
        return response.Headers.Location?.Segments[^1]
            ?? (string?)(await client.FindUserAsync(email, ct))?["id"]
            ?? throw new KeycloakAdminException("user.create", (int)response.StatusCode);
    }
}
