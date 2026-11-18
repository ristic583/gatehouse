using System.Text.Json;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Credentials;

/// <summary>Card commands and their acks (M8-05) on HQ-Copenhagen and Warehouse-Aarhus.</summary>
public sealed class CardCommandTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

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
    public async Task Activation_queues_command_per_connector_instance_with_grants()
    {
        var ct = TestContext.Current.CancellationToken;
        var cardId = await ArrangeAsync([_hq, _aarhus], ct);

        var commands = await ActivateAsync(cardId, ct);

        Assert.Equal(2, commands.Count);
        Assert.All(commands, c => Assert.Equal(ConnectorCommandTypes.ActivateCard, c.Type));
        Assert.Equal([_aarhus, _hq], commands.Select(c => c.PacsInstanceId).OrderBy(i => i == _hq));
        Assert.Equal(CardState.ActivationPending, await CardStateAsync(cardId, ct));
    }

    [Fact]
    public async Task Activation_without_targets_confirms_immediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var cardId = await ArrangeAsync([], ct);

        var commands = await ActivateAsync(cardId, ct);

        Assert.Empty(commands);
        Assert.Equal(CardState.Active, await CardStateAsync(cardId, ct));
        Assert.True(await AuditedAsync("card.activated", cardId, ct));
    }

    [Fact]
    public async Task Applied_ack_activates_card_and_audits()
    {
        var ct = TestContext.Current.CancellationToken;
        var cardId = await ArrangeAsync([_hq, _aarhus], ct);
        var commands = await ActivateAsync(cardId, ct);

        await AckAsync(commands[0], applied: true, ct);
        Assert.Equal(CardState.ActivationPending, await CardStateAsync(cardId, ct));
        await AckAsync(commands[1], applied: true, ct);

        Assert.Equal(CardState.Active, await CardStateAsync(cardId, ct));
        Assert.True(await AuditedAsync("card.activated", cardId, ct));
    }

    [Fact]
    public async Task Failed_ack_returns_card_to_assigned()
    {
        var ct = TestContext.Current.CancellationToken;
        var cardId = await ArrangeAsync([_hq], ct);
        var commands = await ActivateAsync(cardId, ct);

        await AckAsync(Assert.Single(commands), applied: false, ct);

        Assert.Equal(CardState.Assigned, await CardStateAsync(cardId, ct));
        Assert.True(await AuditedAsync("card.activation_failed", cardId, ct));
    }

    [Fact]
    public async Task Set_pin_skips_instances_without_pin_write()
    {
        var ct = TestContext.Current.CancellationToken;
        var cardId = await ArrangeAsync([_hq, _aarhus], ct);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct);

        var ids = await scope.ServiceProvider.GetRequiredService<CardCommandService>().QueueSetPinAsync(card.PersonId, card.CardNumber, "vault:AAAA:local-v1", ct);
        await db.SaveChangesAsync(ct);

        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == Assert.Single(ids), ct);
        Assert.Equal(_aarhus, command.PacsInstanceId);
        Assert.Equal(ConnectorCommandTypes.SetPin, command.Type);
    }

    private static async Task SetInstanceAsync(TenantDbContext db, PacsInstanceId id, bool pinWrite)
    {
        var caps = JsonSerializer.Serialize(new PacsCapabilities("Test", "1.0.0", true, pinWrite, false, false, true, [], 50), ConnectorJson.Options);
        await db.PacsInstances.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector).SetProperty(p => p.CapabilitiesJson, caps));
    }

    // A person with an Assigned card and one Confirmed grant per given instance.
    private async Task<CredentialId> ArrangeAsync(IReadOnlyList<PacsInstanceId> grantsOn, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var person = MappingData.Employee($"cc-{Guid.NewGuid():N}@x.test");
        db.Add(person);
        _people.Add(person.Id);
        foreach (var instanceId in grantsOn)
        {
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Card command test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, instanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        }

        var card = Card.Create(person.Id, Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture), now).Value;
        card.ClearDomainEvents();
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return card.Id;
    }

    private async Task<List<ConnectorCommand>> ActivateAsync(CredentialId cardId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var card = await db.Cards.SingleAsync(c => c.Id == cardId, ct);
        var result = await scope.ServiceProvider.GetRequiredService<CardCommandService>().ActivateAsync(card, card.PersonId, DateTimeOffset.UtcNow, ct);
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync(ct);
        var ids = result.Value.ToList();
        return await db.ConnectorCommands.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.CreatedAt).ToListAsync(ct);
    }

    private async Task AckAsync(ConnectorCommand command, bool applied, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var row = await db.ConnectorCommands.SingleAsync(c => c.Id == command.Id, ct);
        row.Deliver(now);
        row.ApplyAck(applied ? CommandAckStatus.Applied : CommandAckStatus.Failed, null, applied ? null : "pacs.rejected", applied ? null : "Rejected", false, now);
        await db.SaveChangesAsync(ct);

        var ack = new ConnectorCommandAcked(Tenant, command.PacsInstanceId, new ConnectorId(Guid.CreateVersion7()), now, command.Id, null,
            applied ? AckStatus.Applied : AckStatus.Failed, null, applied ? null : new AckError("pacs.rejected", "Rejected", false), true);
        var result = await scope.ServiceProvider.GetRequiredService<ApplyCardCommandAckHandler>().HandleAsync(ack, ct);
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync(ct);
    }

    private async Task<CardState> CardStateAsync(CredentialId id, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.Cards.AsNoTracking().Where(c => c.Id == id).Select(c => c.State).SingleAsync(ct);
    }

    private async Task<bool> AuditedAsync(string action, CredentialId id, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == action && a.SubjectId == id.Value.ToString(), ct);
    }
}
