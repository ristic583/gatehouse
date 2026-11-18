using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Npgsql;

namespace Gatehouse.Adapters.LegacyPacs;

public sealed class LegacyPacsAdapter : IPacsAdapter, IAsyncDisposable
{
    public const string CursorName = "legacypacs.event_log";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly LegacyPacsAdapterOptions _options;
    private readonly IAdapterCursorStore _cursors;
    private readonly NpgsqlDataSource _db;

    public LegacyPacsAdapter(LegacyPacsAdapterOptions options, IAdapterCursorStore cursors)
    {
        _options = options;
        _cursors = cursors;
        _db = NpgsqlDataSource.Create(options.ConnectionString);
    }

    public PacsCapabilities Capabilities { get; } = new(
        "LegacyPacs", "1.0", NativeValidity: true, PinWrite: false, PhotoWrite: false, PushEvents: false, RecordExport: true,
        [DoorCommandKind.MomentaryUnlock, DoorCommandKind.Lockdown], MaxBatchSize: 50);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    public Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct) =>
        WriteAsync(
            "UpsertCardholder", new { cardholder.PersonRef, cardholder.GivenName, cardholder.FamilyName, cardholder.CardNumber }, ct);

    public Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct) =>
        WriteAsync(
            "GrantAccess", new { grant.PersonRef, grant.CardNumber, grant.VendorAccessLevelRef, grant.ValidFrom, grant.ValidTo }, ct);

    public Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct) =>
        WriteAsync("RevokeAccess", new { revoke.PersonRef, revoke.CardNumber, revoke.VendorAccessLevelRef }, ct);

    public Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct) =>
        WriteAsync("SetCardState", new { change.CardNumber, State = change.TargetState }, ct);

    public Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct) =>
        Task.FromResult(AdapterResult.Unsupported("pinWrite"));

    public Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct) =>
        WriteAsync(
            "DoorCommand", new { command.Kind, command.PacsDoorRefs, command.DurationSeconds, command.LockdownAction, command.Reason }, ct);

    public async IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cursor = long.TryParse(await _cursors.GetAsync(CursorName, ct), out var parsed) ? parsed : 0;
            var batch = new List<(long Id, PacsDoorEvent Event)>();
            try
            {
                await using var cmd = _db.CreateCommand(
                    "SELECT id, occurred_at, type, door_ref, reader_ref, card_number, person_ref, direction FROM event_log WHERE "
                        + "id > $1 ORDER BY id LIMIT 100");
                cmd.Parameters.AddWithValue(cursor);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    if (!Enum.TryParse<DoorEventType>(r.GetString(2), true, out var type))
                    {
                        batch.Add((r.GetInt64(0), null!));
                        continue;
                    }

                    var direction = !r.IsDBNull(7) && Enum.TryParse<ReaderDirection>(r.GetString(7), true, out var dir)
                        ? dir
                        : (ReaderDirection?)null;
                    batch.Add((r.GetInt64(0), new PacsDoorEvent(EventGuid(r.GetInt64(0)), r.GetFieldValue<DateTimeOffset>(1), type,
                        r.IsDBNull(3) ? string.Empty : r.GetString(3), Opt(r, 4), Opt(r, 5), Opt(r, 6), direction)));
                }
            }
            catch (NpgsqlException)
            {
                // connection problem: retry on next tick
            }

            foreach (var (id, ev) in batch)
            {
                if (ev is not null)
                {
                    yield return ev;
                }

                await _cursors.SetAsync(CursorName, id.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
            }

            if (batch.Count == 0)
            {
                try
                {
                    await Task.Delay(_options.PollInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }
        }
    }

    public async IAsyncEnumerable<PacsRecord> ExportRecordsAsync(ExportScope scope, [EnumeratorCancellation] CancellationToken ct)
    {
        if (scope.RecordTypes.Contains(RecordType.Cardholder))
        {
            await using var cmd = _db.CreateCommand("SELECT person_ref FROM cardholders ORDER BY person_ref");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                yield return new PacsRecord(RecordType.Cardholder, r.GetString(0), r.GetString(0), null, null, null, null, true);
            }
        }

        if (scope.RecordTypes.Contains(RecordType.Card))
        {
            await using var cmd = _db.CreateCommand("SELECT card_number, person_ref, state FROM cards ORDER BY card_number");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                yield return new PacsRecord(RecordType.Card, r.GetString(0), r.GetString(1), r.GetString(0), null, null, null,
                    string.Equals(r.GetString(2), "Active", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (scope.RecordTypes.Contains(RecordType.AccessAssignment))
        {
            await using var cmd = _db.CreateCommand(
                "SELECT id, person_ref, card_number, access_level_ref, valid_from, valid_to, is_active FROM "
                    + "access_assignments ORDER BY id");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var level = r.GetString(3);
                if (scope.VendorAccessLevelRefs is { } levels && !levels.Contains(level))
                {
                    continue;
                }

                yield return new PacsRecord(RecordType.AccessAssignment, "assignment-" + r.GetInt64(0), r.GetString(1), Opt(r, 2), level,
                    r.IsDBNull(4)
                        ? null
                        : r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5)
                        ? null
                        : r.GetFieldValue<DateTimeOffset>(5), r.GetBoolean(6));
            }
        }
    }

    private async Task<AdapterResult> WriteAsync(string kind, object payload, CancellationToken ct)
    {
        try
        {
            long id;
            await using (var insert = _db.CreateCommand(
                "INSERT INTO integration_inbox (kind, payload) VALUES ($1, $2::jsonb) RETURNING id"))
            {
                insert.Parameters.AddWithValue(kind);
                insert.Parameters.AddWithValue(JsonSerializer.Serialize(payload, Json));
                id = (long)(await insert.ExecuteScalarAsync(ct))!;
            }

            var deadline = DateTime.UtcNow + _options.ProcessingTimeout;
            while (true)
            {
                await using var poll = _db.CreateCommand("SELECT processed_at IS NOT NULL, error FROM integration_inbox WHERE id = $1");
                poll.Parameters.AddWithValue(id);
                await using (var r = await poll.ExecuteReaderAsync(ct))
                {
                    if (await r.ReadAsync(ct) && r.GetBoolean(0))
                    {
                        return Map(id, r.IsDBNull(1) ? null : r.GetString(1));
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return AdapterResult.Failed("legacypacs.timeout", $"Inbox row {id} not processed in time", true);
                }

                await Task.Delay(_options.PollInterval, ct);
            }
        }
        catch (NpgsqlException ex)
        {
            return AdapterResult.Failed("legacypacs.transient", "Database error: " + ex.GetType().Name, true);
        }
    }

    private static AdapterResult Map(long id, string? error)
    {
        if (error is null)
        {
            return AdapterResult.Applied($"inbox-row-{id}");
        }

        if (error.StartsWith("TRANSIENT:", StringComparison.Ordinal))
        {
            return AdapterResult.Failed("legacypacs.transient", error, true);
        }

        return error.StartsWith("UNKNOWN_ACCESS_LEVEL", StringComparison.Ordinal)
            ? AdapterResult.Failed("legacypacs.unknown_access_level", error, false)
            : AdapterResult.Failed("legacypacs.rejected", error, false);
    }

    private static string? Opt(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static Guid EventGuid(long id)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, id);
        "legacypa"u8.CopyTo(bytes[8..]);
        return new Guid(bytes);
    }
}
