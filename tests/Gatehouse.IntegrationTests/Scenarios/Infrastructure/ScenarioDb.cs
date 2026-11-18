using Npgsql;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Read-only queries on the Nordlys tenant database for assertions the API does not expose.</summary>
public sealed class ScenarioDb(NpgsqlDataSource tenantDb)
{
    public async Task<IReadOnlyList<FulfillmentRow>> FulfillmentsAsync(Guid entitlementId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand(
            "SELECT id, kind, status, pacs_instance_id, pacs_ref FROM fulfillments WHERE entitlement_id = $1 ORDER BY created_at");
        command.Parameters.AddWithValue(entitlementId);
        var rows = new List<FulfillmentRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new FulfillmentRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    /// <summary>The newest entitlement of the person on the area in the status (e.g. the renewal the lifecycle opened), or null.</summary>
    public async Task<Guid?> EntitlementAsync(Guid personId, Guid areaId, string status, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand(
            "SELECT id FROM entitlements WHERE person_id = $1 AND area_id = $2 AND status = $3 ORDER BY created_at DESC LIMIT 1");
        command.Parameters.AddWithValue(personId);
        command.Parameters.AddWithValue(areaId);
        command.Parameters.AddWithValue(status);
        return await command.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    /// <summary>Number of entitlements of the person on the area in the status.</summary>
    public async Task<long> CountEntitlementsAsync(Guid personId, Guid areaId, string status, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT count(*) FROM entitlements WHERE person_id = $1 AND area_id = $2 AND status = $3");
        command.Parameters.AddWithValue(personId);
        command.Parameters.AddWithValue(areaId);
        command.Parameters.AddWithValue(status);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Last heartbeat of a PACS instance, or null.</summary>
    public async Task<DateTimeOffset?> LastHeartbeatAsync(string pacsInstanceName, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT last_heartbeat_at FROM pacs_instances WHERE name = $1");
        command.Parameters.AddWithValue(pacsInstanceName);
        return await command.ExecuteScalarAsync(ct) is DateTime at ? new DateTimeOffset(at, TimeSpan.Zero) : null;
    }

    /// <summary>Effective end of an entitlement.</summary>
    public async Task<DateTimeOffset> EffectiveEndAsync(Guid entitlementId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT effective_end FROM entitlements WHERE id = $1");
        command.Parameters.AddWithValue(entitlementId);
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(ct))!, TimeSpan.Zero);
    }

    /// <summary>Status and last error of the connector commands of a door command (one per door).</summary>
    public async Task<IReadOnlyList<(string Status, string? LastError)>> DoorConnectorCommandsAsync(Guid doorCommandId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT status, last_error FROM connector_commands WHERE idempotency_key LIKE $1");
        command.Parameters.AddWithValue($"%:{doorCommandId}:%");
        var rows = new List<(string, string?)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>Kind and status of the staff tasks about a subject (fulfillment, finding, …), oldest first.</summary>
    public async Task<IReadOnlyList<(Guid TaskId, string Kind, string Status)>> TasksForSubjectAsync(Guid subjectId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT id, kind, status FROM staff_tasks WHERE subject_id = $1 ORDER BY created_at");
        command.Parameters.AddWithValue(subjectId);
        var rows = new List<(Guid, string, string)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>The staff task of the newest reconciliation finding about the person, or null.</summary>
    public async Task<Guid?> FindingTaskAsync(Guid personId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand(
            "SELECT staff_task_id FROM reconciliation_findings WHERE person_id = $1 AND staff_task_id IS NOT NULL ORDER BY created_at DESC LIMIT 1");
        command.Parameters.AddWithValue(personId);
        return await command.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    /// <summary>Status of a staff task.</summary>
    public async Task<string?> TaskStatusAsync(Guid taskId, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT status FROM staff_tasks WHERE id = $1");
        command.Parameters.AddWithValue(taskId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Subject ids of staff tasks of a kind that are not resolved.</summary>
    public async Task<IReadOnlyList<Guid>> OpenTaskSubjectsAsync(string kind, CancellationToken ct)
    {
        await using var command = tenantDb.CreateCommand("SELECT subject_id FROM staff_tasks WHERE kind = $1 AND status <> 'Resolved' ORDER BY created_at");
        command.Parameters.AddWithValue(kind);
        var rows = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(reader.GetGuid(0));
        }

        return rows;
    }

    /// <summary>Waits until the PACS instance reported a heartbeat after <paramref name="since"/> (a stale one from an earlier run does not count).</summary>
    public async Task WaitForHeartbeatAsync(string pacsInstanceName, DateTimeOffset since, TimeSpan timeout, CancellationToken ct)
    {
        var fresh = await PacsProbe.WaitUntilAsync(
            async c =>
            {
                try
                {
                    await using var command = tenantDb.CreateCommand("SELECT 1 FROM pacs_instances WHERE name = $1 AND last_heartbeat_at >= $2");
                    command.Parameters.AddWithValue(pacsInstanceName);
                    command.Parameters.AddWithValue(since);
                    return await command.ExecuteScalarAsync(c) is not (null or DBNull);
                }
                catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.TooManyConnections)
                {
                    // Every service opens its pools while the AppHost starts; the burst passes.
                    return false;
                }
            },
            timeout,
            ct);
        if (!fresh)
        {
            throw new TimeoutException($"{pacsInstanceName} reported no heartbeat within {timeout}.");
        }
    }
}
