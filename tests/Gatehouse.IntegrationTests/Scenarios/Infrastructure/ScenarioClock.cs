using Npgsql;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Moves windows into the past or near future by writing the tenant database, then runs the lifecycle job (M11 decision 4).</summary>
public sealed class ScenarioClock(NpgsqlDataSource tenantDb)
{
    public async Task SetEntitlementWindowAsync(Guid entitlementId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand(
            "UPDATE entitlements SET requested_start = $2, requested_end = $3, effective_end = $3 WHERE id = $1");
        command.Parameters.AddWithValue(entitlementId);
        command.Parameters.AddWithValue(from.ToUniversalTime());
        command.Parameters.AddWithValue(to.ToUniversalTime());
        await ExpectOneRowAsync(command, $"entitlement {entitlementId}", ct);
    }

    public async Task SetEligibilityEndAsync(Guid personId, DateTimeOffset end, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("UPDATE persons SET eligibility_end = $2 WHERE id = $1");
        command.Parameters.AddWithValue(personId);
        command.Parameters.AddWithValue(end.ToUniversalTime());
        await ExpectOneRowAsync(command, $"person {personId}", ct);
    }

    /// <summary><c>POST /api/v1/admin/lifecycle/run</c> as the given (tenant admin) session; returns the run summary.</summary>
    public static Task<System.Text.Json.JsonElement> RunLifecycleAsync(ScenarioClient admin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(admin);
        return admin.PostAsync<System.Text.Json.JsonElement>("v1/admin/lifecycle/run", null, ct);
    }

    private static async Task ExpectOneRowAsync(NpgsqlCommand command, string what, CancellationToken ct)
    {
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException($"No row updated for {what}.");
        }
    }
}
