using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gatehouse.Application.Credentials;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Credentials;

/// <summary><c>PUT /api/v1/me/pin</c> (M8-11) for a fresh employee with an Active card; HQ has no PIN write, Aarhus has.</summary>
public sealed class SetPinTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri MyPin = new("/api/v1/me/pin", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];
    private readonly List<(PacsInstanceId Id, FulfillmentMode Mode, string? Caps)> _instances = [];
    private PacsInstanceId _hq;
    private PacsInstanceId _aarhus;
    private Domain.Sites.AreaId _areaId;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var name in new[] { "HQ-Copenhagen", "Warehouse-Aarhus" })
        {
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == name);
            _instances.Add((instance.Id, instance.FulfillmentMode, instance.CapabilitiesJson));
        }

        (_hq, _aarhus) = (_instances[0].Id, _instances[1].Id);
        _areaId = (await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT")).Id;
        await SetInstanceAsync(db, _hq, pinWrite: false);
        await SetInstanceAsync(db, _aarhus, pinWrite: true);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Set_pin_stores_ciphertext_not_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await ArrangeAsync(ct);
        using (client)
        {
            using var invalid = await client.PutAsJsonAsync(MyPin, new { pin = "12ab" }, ct);
            using var response = await client.PutAsJsonAsync(MyPin, new { pin = "583920" }, ct);

            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("pin.invalid", await invalid.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.PinCredentials.AsNoTracking().SingleAsync(p => p.Id == personId, ct);
        Assert.Equal("local-v1", stored.KeyId);
        Assert.DoesNotContain("583920", Encoding.UTF8.GetString(stored.Ciphertext), StringComparison.Ordinal);
        Assert.Equal("583920", (await scope.ServiceProvider.GetRequiredService<IPinVault>().DecryptAsync(stored.Ciphertext, stored.KeyId, ct)).Reveal());
        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "pin.set" && a.SubjectId == personId.Value.ToString(), ct);
        Assert.DoesNotContain("583920", audit.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_pin_queues_command_only_for_pin_write_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await ArrangeAsync(ct);
        using (client)
        {
            using var response = await client.PutAsJsonAsync(MyPin, new { pin = "4821" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var commands = await SetPinCommandsAsync(personId, ct);
        var command = Assert.Single(commands);
        Assert.Equal(_aarhus, command.PacsInstanceId);
    }

    [Fact]
    public async Task Stored_command_never_contains_plain_pin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await ArrangeAsync(ct);
        using (client)
        {
            using var response = await client.PutAsJsonAsync(MyPin, new { pin = "60917342" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var command = Assert.Single(await SetPinCommandsAsync(personId, ct));
        var payload = JsonSerializer.Deserialize<Connector.Contracts.Commands.SetPin>(command.PayloadJson, ConnectorJson.Options)!;
        Assert.StartsWith("vault:", payload.EncryptedPin, StringComparison.Ordinal);
        Assert.EndsWith(":local-v1", payload.EncryptedPin, StringComparison.Ordinal);
        Assert.DoesNotContain("60917342", command.PayloadJson, StringComparison.Ordinal);
    }

    private async Task<List<Domain.Connectors.ConnectorCommand>> SetPinCommandsAsync(PersonId personId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var partition = $"person:{personId.Value}";
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking()
            .Where(c => c.PartitionKey == partition && c.Type == ConnectorCommandTypes.SetPin).ToListAsync(ct);
    }

    private static async Task SetInstanceAsync(TenantDbContext db, PacsInstanceId id, bool pinWrite)
    {
        var caps = JsonSerializer.Serialize(new PacsCapabilities("Test", "1.0.0", true, pinWrite, false, false, true, [], 50), ConnectorJson.Options);
        await db.PacsInstances.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector).SetProperty(p => p.CapabilitiesJson, caps));
    }

    // An employee with an Active card and one Confirmed grant on each instance, plus an API client signed in as them.
    private async Task<(PersonId PersonId, HttpClient Client)> ArrangeAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var email = $"pin-{Guid.NewGuid():N}@nordlys.test";
        var person = MappingData.Employee(email);
        person.ClearDomainEvents();
        db.Add(person);
        _people.Add(person.Id);
        foreach (var instanceId in new[] { _hq, _aarhus })
        {
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "PIN test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, instanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        }

        var card = Card.CreateActive(person.Id, Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture), now).Value;
        card.ClearDomainEvents();
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return (person.Id, _api.CreateClientFor($"sub-{Guid.NewGuid():N}", email, Tenant));
    }
}
