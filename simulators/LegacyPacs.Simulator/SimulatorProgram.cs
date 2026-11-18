using Npgsql;

namespace LegacyPacs.Simulator;

public sealed record ChaosRequest(int FailNext, int FailNextPermanent, int DelayMs);

public static class SimulatorProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = builder.Configuration.GetConnectionString("legacypacs")
            ?? throw new InvalidOperationException("Connection string 'legacypacs' is not configured.");
        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DoorController>();
        builder.Services.AddSingleton<InboxProcessor>();
        builder.Services.AddHostedService<InboxProcessorService>();

        var app = builder.Build();
        await LegacyPacsSchema.EnsureAsync(dataSource, CancellationToken.None);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/chaos", async (ChaosRequest request, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var command = db.CreateCommand("UPDATE chaos_settings SET fail_next = $1, fail_next_permanent = $2, delay_ms = $3 WHERE id = 1");
            command.Parameters.AddWithValue(request.FailNext);
            command.Parameters.AddWithValue(request.FailNextPermanent);
            command.Parameters.AddWithValue(request.DelayMs);
            await command.ExecuteNonQueryAsync(ct);
            return Results.NoContent();
        });
        app.MapDelete("/chaos", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var command = db.CreateCommand("UPDATE chaos_settings SET fail_next = 0, fail_next_permanent = 0, delay_ms = 0 WHERE id = 1");
            await command.ExecuteNonQueryAsync(ct);
            return Results.NoContent();
        });
        app.MapGet("/assignments", async (string? personRef, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var command = db.CreateCommand("SELECT person_ref, card_number, access_level_ref, valid_from, valid_to, is_active FROM access_assignments WHERE ($1::text IS NULL OR person_ref = $1) ORDER BY id");
            command.Parameters.AddWithValue((object?)personRef ?? DBNull.Value);
            var rows = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new
                {
                    personRef = reader.GetString(0),
                    cardNumber = reader.IsDBNull(1) ? null : reader.GetString(1),
                    accessLevelRef = reader.GetString(2),
                    validFrom = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
                    validTo = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4),
                    isActive = reader.GetBoolean(5),
                });
            }

            return Results.Ok(rows);
        });

        app.MapPost("/controller/swipe", async (SwipeRequest request, DoorController doors, CancellationToken ct) =>
            await doors.SwipeAsync(request, ct) is { } result ? Results.Ok(result) : Results.NotFound());
        app.MapPost("/controller/door-events", async (DoorEventRequest request, DoorController doors, CancellationToken ct) =>
        {
            if (!DoorController.EventTypes.Contains(request.Type, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { error = "UNKNOWN_EVENT_TYPE" });
            }

            return await doors.RecordDoorEventAsync(request.DoorRef, request.Type, ct) is { } eventId
                ? Results.Ok(new { eventId })
                : Results.NotFound();
        });
        app.MapGet("/doors", async (DoorController doors, CancellationToken ct) => Results.Ok(await doors.ListDoorsAsync(ct)));

        await app.RunAsync();
    }
}
