using System.Net.Http.Json;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Identity;

/// <summary>First sign-in race (found by the M9 guest e2e): parallel first requests all resolve the person.</summary>
public sealed class IdentityLinkRaceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);
    private PersonId? _person;

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Parallel_first_requests_all_resolve_the_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"race-{Guid.NewGuid():N}@nordlys.test";
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = MappingData.Employee(email);
            person.ClearDomainEvents();
            db.Persons.Add(person);
            await db.SaveChangesAsync(ct);
            _person = person.Id;
        }

        var sub = $"sub-{Guid.NewGuid():N}";
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var client = _api.CreateClientFor(sub, email, "nordlys");
            return await client.GetFromJsonAsync<MeDto>(new Uri("/api/v1/me", UriKind.Relative), ApiJson.Options, ct);
        }));

        Assert.All(results, me => Assert.Equal(_person!.Value.Value, me!.Person!.PersonId));
    }
}
