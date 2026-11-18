using System.Text.Json;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.People.Termination;
using Gatehouse.Connector.Contracts;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

public sealed class TerminationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);
    private LifecycleArrange _arrange = null!;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _arrange = new LifecycleArrange(_api);
        await _arrange.InitializeAsync();
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Termination_revokes_entitlements_with_high_priority_revokes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, _, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 2, ct);

        await TerminateAsync(personId, ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => e.PersonId == personId).ToListAsync(ct);
        Assert.Equal(2, entitlements.Count);
        Assert.All(entitlements, e => Assert.Equal(EntitlementStatus.Revoked, e.Status));
        Assert.All(entitlements, e => Assert.Equal(RevocationKind.Termination, e.Revocation?.Kind));
        var ids = entitlements.Select(e => e.Id).ToList();
        var revokes = await db.Fulfillments.AsNoTracking().Where(f => ids.Contains(f.EntitlementId) && f.Kind == FulfillmentKind.Revoke).ToListAsync(ct);
        Assert.Equal(2, revokes.Count);
        Assert.All(revokes, f => Assert.Equal(CommandPriority.High, f.Priority));
        Assert.Equal(PersonStatus.Terminated, (await db.Persons.AsNoTracking().SingleAsync(p => p.Id == personId, ct)).Status);
    }

    [Fact]
    public async Task Termination_revokes_cards_and_queues_suspend_commands()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, cardId, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 1, ct);

        await TerminateAsync(personId, ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(CardState.Revoked, (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct)).State);
        var partition = $"person:{personId.Value}";
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.PartitionKey == partition && c.Type == ConnectorCommandTypes.SuspendCard, ct);
        Assert.Equal(_arrange.Hq, command.PacsInstanceId);
        Assert.Equal(CommandPriority.High, command.Priority);
        Assert.Equal("Revoked", JsonDocument.Parse(command.PayloadJson).RootElement.GetProperty("targetState").GetString());
    }

    [Fact]
    public async Task Termination_revoke_is_dispatched_with_the_revoked_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, cardId, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 1, ct);

        await TerminateAsync(personId, ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlementIds = await db.Entitlements.AsNoTracking().Where(e => e.PersonId == personId).Select(e => e.Id).ToListAsync(ct);
        var revoke = await db.Fulfillments.AsNoTracking().SingleAsync(f => entitlementIds.Contains(f.EntitlementId) && f.Kind == FulfillmentKind.Revoke, ct);
        var sp = scope.ServiceProvider;
        var handler = ActivatorUtilities.CreateInstance<DispatchFulfillmentHandler>(sp, new ConnectorCommandFactory(sp.GetRequiredService<IConnectorSequencer>(), TimeProvider.System), new InMemoryConnectorCommandSignal());
        await handler.HandleAsync(new DispatchFulfillmentCommand(revoke.Id), ct);
        await db.SaveChangesAsync(ct);

        var card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct);
        Assert.Equal(FulfillmentStatus.Pending, (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == revoke.Id, ct)).Status);
        var partition = $"person:{personId.Value}";
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.PartitionKey == partition && c.Type == ConnectorCommandTypes.RevokeAccess, ct);
        Assert.Equal(card.CardNumber, JsonDocument.Parse(command.PayloadJson).RootElement.GetProperty("cardNumber").GetString());
        Assert.Equal("Termination", JsonDocument.Parse(command.PayloadJson).RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Termination_is_audited_with_revoked_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, cardId, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 1, ct);

        await TerminateAsync(personId, ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlementId = (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == personId, ct)).Id.Value.ToString();
        var subject = personId.Value.ToString();
        var entry = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "person.terminated" && a.SubjectId == subject, ct);
        Assert.Equal("termination", entry.ActorId);
        Assert.Contains(entitlementId, entry.Payload, StringComparison.Ordinal);
        Assert.Contains(cardId.Value.ToString(), entry.Payload, StringComparison.Ordinal);
        var card = cardId.Value.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "card.revoked" && a.SubjectId == card, ct));
    }

    [Fact]
    public async Task Admin_terminates_employee_through_api()
    {
        var ct = TestContext.Current.CancellationToken;
        var employee = await EmployeeAsync(ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var uri = new Uri($"/api/v1/admin/people/{employee.Value}/terminate", UriKind.Relative);

        using var forbidden = await anna.PostAsync(uri, null, ct);
        using var ok = await freja.PostAsync(uri, null, ct);
        using var again = await freja.PostAsync(uri, null, ct);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, again.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(PersonStatus.Terminated, (await db.Persons.AsNoTracking().SingleAsync(p => p.Id == employee, ct)).Status);
    }

    [Fact]
    public async Task Admin_terminate_refuses_external()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, _, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 0, ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await freja.PostAsync(new Uri($"/api/v1/admin/people/{personId.Value}/terminate", UriKind.Relative), null, ct);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<PersonId> EmployeeAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var person = Person.Create(PersonSource.Authoritative, PersonType.Employee, "Leaver", "Test", $"leaver-{Guid.NewGuid():N}@x.test",
            Eligibility.Create(now.AddYears(-1), null, "Europe/Copenhagen").Value, null, null, now).Value;
        person.ClearDomainEvents();
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return person.Id;
    }

    private async Task TerminateAsync(PersonId personId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TerminationService>();
        var person = await db.Persons.SingleAsync(p => p.Id == personId, ct);
        var outcome = await service.TerminateAsync(person, AuditActor.ForSystem("termination"), DateTimeOffset.UtcNow, ct);
        Assert.True(outcome.IsSuccess);
        await db.SaveChangesAsync(ct);
        await service.NotifyAsync(outcome.Value, ct);
    }
}
