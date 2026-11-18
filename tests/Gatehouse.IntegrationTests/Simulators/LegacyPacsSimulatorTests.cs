using Gatehouse.IntegrationTests.Infrastructure;
using LegacyPacs.Simulator;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Gatehouse.IntegrationTests.Simulators;

public sealed class LegacyPacsSimulatorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private NpgsqlDataSource _db = null!;

    public async ValueTask InitializeAsync()
    {
        _db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("legacypacs")));
        await LegacyPacsSchema.EnsureAsync(_db, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Grant_row_creates_active_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertAsync("GrantAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-SERVER","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""");
        Assert.Equal(1, await new InboxProcessor(_db).ProcessOnceAsync(ct));
        Assert.Null(await ErrorAsync(1));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM access_assignments WHERE person_ref = 'p1' AND is_active"));
    }

    [Fact]
    public async Task Unknown_level_sets_error()
    {
        await InsertAsync("GrantAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-NOPE","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""");
        await new InboxProcessor(_db).ProcessOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal("UNKNOWN_ACCESS_LEVEL", await ErrorAsync(1));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM access_assignments"));
    }

    [Fact]
    public async Task Fail_next_sets_transient_error_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await ScalarAsync("UPDATE chaos_settings SET fail_next = 1 WHERE id = 1 RETURNING 1");
        const string grant = """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-LAB","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""";
        await InsertAsync("GrantAccess", grant);
        await InsertAsync("GrantAccess", grant);
        await new InboxProcessor(_db).ProcessOnceAsync(ct);
        Assert.StartsWith("TRANSIENT", await ErrorAsync(1), StringComparison.Ordinal);
        Assert.Null(await ErrorAsync(2));
    }

    [Fact]
    public async Task Revoke_deactivates_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertAsync("GrantAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-LAB","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""");
        await InsertAsync("RevokeAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-LAB","kind":"Normal"}""");
        await new InboxProcessor(_db).ProcessOnceAsync(ct);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM access_assignments WHERE person_ref = 'p1' AND NOT is_active"));
    }

    [Fact]
    public async Task Swipe_with_active_assignment_is_granted()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        await SeedHolderAsync(time);
        var result = await new DoorController(_db, time).SwipeAsync(new SwipeRequest("D-CPH-LAB", CardNumber: "100", Direction: "In"), ct);
        Assert.NotNull(result);
        Assert.Equal("Granted", result.Decision);
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM event_log WHERE id = {result.EventId} AND type = 'AccessGranted' AND reader_ref = 'R-CPH-LAB' AND person_ref = 'p1' AND card_number = '100'"));
        var byName = await new DoorController(_db, time).SwipeAsync(new SwipeRequest("D-CPH-LAB", HolderName: "Ana Berg"), ct);
        Assert.Equal("Granted", byName!.Decision);
        Assert.Null(await new DoorController(_db, time).SwipeAsync(new SwipeRequest("D-NOPE", CardNumber: "100"), ct));
    }

    [Fact]
    public async Task Swipe_without_assignment_is_denied()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        await SeedHolderAsync(time);
        var doors = new DoorController(_db, time);
        var otherDoor = await doors.SwipeAsync(new SwipeRequest("D-CPH-VAULT", PersonRef: "p1"), ct);
        Assert.Equal("Denied", otherDoor!.Decision);
        time.Advance(TimeSpan.FromDays(30));
        var expired = await doors.SwipeAsync(new SwipeRequest("D-CPH-LAB", CardNumber: "100"), ct);
        Assert.Equal("Denied", expired!.Decision);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM event_log WHERE type = 'AccessDenied'"));
    }

    [Fact]
    public async Task Lockdown_command_writes_events_and_denies_swipes()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        await SeedHolderAsync(time);
        await InsertAsync("DoorCommand", """{"kind":"Lockdown","pacsDoorRefs":["D-CPH-LAB","D-CPH-LOBBY"],"durationSeconds":null,"lockdownAction":"Start","reason":"drill"}""");
        await InsertAsync("DoorCommand", """{"kind":"Lockdown","pacsDoorRefs":["D-NOPE"],"lockdownAction":"Start"}""");
        await new InboxProcessor(_db, time).ProcessOnceAsync(ct);
        Assert.Null(await ErrorAsync(4));
        Assert.StartsWith("UNKNOWN_DOOR", await ErrorAsync(5), StringComparison.Ordinal);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM event_log WHERE type = 'LockdownStarted'"));
        var doors = new DoorController(_db, time);
        Assert.Contains(await doors.ListDoorsAsync(ct), d => d.DoorRef == "D-CPH-LAB" && d.Lockdown);
        Assert.Equal("Denied", (await doors.SwipeAsync(new SwipeRequest("D-CPH-LAB", CardNumber: "100"), ct))!.Decision);

        await InsertAsync("DoorCommand", """{"kind":"Lockdown","pacsDoorRefs":["D-CPH-LAB"],"lockdownAction":"Clear"}""");
        await new InboxProcessor(_db, time).ProcessOnceAsync(ct);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM event_log WHERE type = 'LockdownCleared' AND door_ref = 'D-CPH-LAB'"));
        Assert.Equal("Granted", (await doors.SwipeAsync(new SwipeRequest("D-CPH-LAB", CardNumber: "100"), ct))!.Decision);
    }

    [Fact]
    public async Task Momentary_unlock_relocks_after_duration()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        await InsertAsync("DoorCommand", """{"kind":"MomentaryUnlock","pacsDoorRefs":["D-CPH-LAB"],"durationSeconds":5,"lockdownAction":null,"reason":"visitor"}""");
        await new InboxProcessor(_db, time).ProcessOnceAsync(ct);
        Assert.Null(await ErrorAsync(1));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM event_log WHERE type = 'DoorUnlocked' AND door_ref = 'D-CPH-LAB'"));
        var doors = new DoorController(_db, time);
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, await doors.RelockDueAsync(ct));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await doors.RelockDueAsync(ct));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM event_log WHERE type = 'DoorRelocked' AND door_ref = 'D-CPH-LAB'"));
        Assert.Equal(0, await doors.RelockDueAsync(ct));
    }

    private async Task SeedHolderAsync(TimeProvider time)
    {
        await InsertAsync("UpsertCardholder", """{"personRef":"p1","givenName":"Ana","familyName":"Berg","cardNumber":"100"}""");
        await InsertAsync("GrantAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-LAB","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""");
        await InsertAsync("GrantAccess", """{"personRef":"p1","cardNumber":"100","vendorAccessLevelRef":"AL-CPH-LOBBY","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}""");
        await new InboxProcessor(_db, time).ProcessOnceAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertAsync(string kind, string payload)
    {
        await using var command = _db.CreateCommand("INSERT INTO integration_inbox (kind, payload) VALUES ($1, $2::jsonb)");
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(payload);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> ErrorAsync(long id)
    {
        await using var command = _db.CreateCommand("SELECT error FROM integration_inbox WHERE id = $1");
        command.Parameters.AddWithValue(id);
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value as string;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = _db.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
