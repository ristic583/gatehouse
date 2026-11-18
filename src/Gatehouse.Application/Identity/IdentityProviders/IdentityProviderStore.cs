using System.Text.Json;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary><c>tenant_settings</c> key <see cref="Key"/> (JSON, no secrets).</summary>
public sealed class IdentityProviderStore(ITenantDb db)
{
    public const string Key = "identity.provider";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async Task<TenantIdentityProvider?> GetAsync(CancellationToken ct)
    {
        var row = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TenantIdentityProvider>(row.Value, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Stages the change; the caller saves.</summary>
    public async Task SetAsync(TenantIdentityProvider provider, DateTimeOffset now, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(provider, Json);
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            db.TenantSettings.Add(new TenantSetting(Key, value, now));
        }
        else
        {
            row.Update(value, now);
        }
    }

    /// <summary>Stages the removal; the caller saves.</summary>
    public async Task RemoveAsync(CancellationToken ct)
    {
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is not null)
        {
            db.TenantSettings.Remove(row);
        }
    }
}
