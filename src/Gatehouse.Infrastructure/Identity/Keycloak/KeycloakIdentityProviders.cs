using System.Net;
using System.Text.Json.Nodes;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>
/// The tenant's single brokered identity provider: the instance, its mappers and its link to the tenant's organization.
/// </summary>
internal sealed class KeycloakIdentityProviders(KeycloakAdminClient client, ILogger logger)
{
    /// <summary>Creates or updates <paramref name="provider"/>, then removes <paramref name="otherAlias"/> (the other kind).</summary>
    public async Task UpsertAsync(TenantId tenantId, ProviderDefinition provider, string otherAlias, CancellationToken ct)
    {
        var org = await client.FindOrganizationAsync(tenantId, ct) ?? throw new KeycloakAdminException("organization.find", 404);
        var config = provider.Config;
        config["kc.org.domain"] = (string?)org["domains"]?.AsArray().FirstOrDefault()?["name"];
        config["kc.org.broker.redirect.mode.email-matches"] = "true";

        await SaveInstanceAsync(provider, ct);
        await SaveMappersAsync(provider.Alias, provider.Mappers, ct);

        // One provider per tenant: switching kind removes the previous one, but only once the new one and its mappers exist, so a
        // failed replacement leaves the working provider in place (GH-ADMIN-03).
        await DeleteAsync(tenantId, otherAlias, ct);

        var linkPath = $"organizations/{(string?)org["id"]}/identity-providers";
        using var link = await client.SendRawAsync(HttpMethod.Post, linkPath, JsonValue.Create(provider.Alias), ct);
        if (link.StatusCode != HttpStatusCode.Conflict)
        {
            KeycloakAdminClient.Ensure(link, "organization.link_identity_provider");
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Identity provider {Alias} saved for tenant {TenantId}", provider.Alias, tenantId.Value);
        }
    }

    public async Task<bool> ExistsAsync(string alias, CancellationToken ct) =>
        await client.GetJsonAsync($"identity-provider/instances/{alias}", "identity_provider.get", ct) is not null;

    public async Task<bool> DeleteAsync(TenantId tenantId, string alias, CancellationToken ct)
    {
        if (!await ExistsAsync(alias, ct))
        {
            return false;
        }

        if (await client.FindOrganizationAsync(tenantId, ct) is { } org)
        {
            var unlinkPath = $"organizations/{(string?)org["id"]}/identity-providers/{alias}";
            using var unlink = await client.SendRawAsync(HttpMethod.Delete, unlinkPath, null, ct);
            if (unlink.StatusCode != HttpStatusCode.NotFound)
            {
                KeycloakAdminClient.Ensure(unlink, "organization.unlink_identity_provider");
            }
        }

        using var delete = await client.SendRawAsync(HttpMethod.Delete, $"identity-provider/instances/{alias}", null, ct);
        if (delete.StatusCode != HttpStatusCode.NotFound)
        {
            KeycloakAdminClient.Ensure(delete, "identity_provider.delete");
        }

        return true;
    }

    private async Task SaveInstanceAsync(ProviderDefinition provider, CancellationToken ct)
    {
        var alias = provider.Alias;
        var config = provider.Config;
        var representation = new JsonObject
        {
            ["alias"] = alias,
            ["displayName"] = provider.DisplayName,
            ["providerId"] = provider.ProviderId,
            ["enabled"] = true,
            ["trustEmail"] = true,
            ["storeToken"] = false,
            ["linkOnly"] = false,
            ["hideOnLogin"] = true,
            ["firstBrokerLoginFlowAlias"] = "first broker login",
            ["updateProfileFirstLoginMode"] = "off",
            ["config"] = config,
        };

        var existing = await client.GetJsonAsync($"identity-provider/instances/{alias}", "identity_provider.get", ct);
        if (existing is null)
        {
            if (config["clientSecret"] is JsonValue secret && (string?)secret == KeycloakProviderConfigs.MaskedSecret)
            {
                config.Remove("clientSecret");
            }

            using var created = await client.SendAsync(HttpMethod.Post, "identity-provider/instances", representation, ct);
            KeycloakAdminClient.Ensure(created, "identity_provider.create");
        }
        else
        {
            representation["internalId"] = existing["internalId"]?.DeepClone();
            using var updated = await client.SendAsync(HttpMethod.Put, $"identity-provider/instances/{alias}", representation, ct);
            KeycloakAdminClient.Ensure(updated, "identity_provider.update");
        }
    }

    private async Task SaveMappersAsync(string alias, List<JsonObject> mappers, CancellationToken ct)
    {
        var mappersPath = $"identity-provider/instances/{alias}/mappers";
        var current = (await client.GetJsonAsync(mappersPath, "identity_provider.mappers", ct))?.AsArray() ?? [];
        foreach (var mapper in mappers)
        {
            mapper["identityProviderAlias"] = alias;
            var match = current.FirstOrDefault(m => (string?)m?["name"] == (string?)mapper["name"]);
            if (match is null)
            {
                using var add = await client.SendAsync(HttpMethod.Post, mappersPath, mapper, ct);
                KeycloakAdminClient.Ensure(add, "identity_provider.mapper_create");
            }
            else
            {
                mapper["id"] = (string?)match["id"];
                using var put = await client.SendAsync(HttpMethod.Put, $"{mappersPath}/{(string?)match["id"]}", mapper, ct);
                KeycloakAdminClient.Ensure(put, "identity_provider.mapper_update");
            }
        }
    }
}
