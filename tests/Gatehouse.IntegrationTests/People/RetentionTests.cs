using System.Net.Http.Json;
using Gatehouse.Application.Audit.Dtos;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.People.Retention;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Storage;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>GDPR retention (M9-06) on fresh people; they are removed afterwards.</summary>
public sealed class RetentionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Photo_deleted_after_retention()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, key) = await PersonWithPhotoAsync(terminatedDaysAgo: 40, ct);

        await RunAsync(ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.False(await db.Photos.AnyAsync(p => p.PersonId == id, ct));
        Assert.DoesNotContain(key, Store.Keys);
        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        Assert.Null(person.AnonymizedAt);
        Assert.Equal("Erik", person.GivenName);
    }

    [Fact]
    public async Task Person_anonymized_after_retention()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, key) = await PersonWithPhotoAsync(terminatedDaysAgo: 400, ct);

        await RunAsync(ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        Assert.NotNull(person.AnonymizedAt);
        Assert.Equal("Anonymized", person.GivenName);
        Assert.StartsWith("anonymized-", person.Email, StringComparison.Ordinal);
        Assert.Null(person.IdentitySubject);
        Assert.False(await db.PinCredentials.AnyAsync(p => p.Id == id, ct));
        Assert.False(await db.Photos.AnyAsync(p => p.PersonId == id, ct));
        Assert.DoesNotContain(key, Store.Keys);
        var subject = id.Value.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "person.anonymized" && a.SubjectId == subject, ct));
    }

    [Fact]
    public async Task Active_people_are_never_touched()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, key) = await PersonWithPhotoAsync(terminatedDaysAgo: null, ct);

        await RunAsync(ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.Photos.AnyAsync(p => p.PersonId == id, ct));
        Assert.Contains(key, Store.Keys);
        Assert.Null((await db.Persons.AsNoTracking().SingleAsync(p => p.Id == id, ct)).AnonymizedAt);
    }

    [Fact]
    public async Task Audit_chain_still_verifies_after_anonymization()
    {
        var ct = TestContext.Current.CancellationToken;
        await PersonWithPhotoAsync(terminatedDaysAgo: 400, ct);

        await RunAsync(ct);

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var verification = await freja.GetFromJsonAsync<AuditVerificationDto>(new Uri("/api/v1/audit/verify", UriKind.Relative), ApiJson.Options, ct);
        Assert.True(verification!.Verified);
    }

    private InMemoryObjectStore Store => (InMemoryObjectStore)_api.Services.GetRequiredService<IObjectStore>();

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        await scope.ServiceProvider.GetRequiredService<RetentionRunner>().RunTenantAsync(DateTimeOffset.UtcNow, ct);
    }

    private async Task<(PersonId Id, string Key)> PersonWithPhotoAsync(int? terminatedDaysAgo, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var person = MappingData.Employee($"rt-{Guid.NewGuid():N}@nordlys.test");
        person.ClearDomainEvents();
        db.Persons.Add(person);
        _people.Add(person.Id);
        var key = $"tenant/{Tenant}/photos/{person.Id.Value}/{Guid.NewGuid():N}.jpg";
        db.Photos.Add(Photo.Create(person.Id, key, Photo.Jpeg, 1000, now).Value);
        db.PinCredentials.Add(PinCredential.Set(person.Id, [1, 2, 3], "local-v1", person.Id, now));
        await db.SaveChangesAsync(ct);
        await Store.PutAsync(key, [1, 2, 3], Photo.Jpeg, ct);
        if (terminatedDaysAgo is { } days)
        {
            var at = now.AddDays(-days);
            await db.Persons.Where(p => p.Id == person.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PersonStatus.Terminated).SetProperty(p => p.TerminatedAt, at), ct);
        }

        return (person.Id, key);
    }
}
