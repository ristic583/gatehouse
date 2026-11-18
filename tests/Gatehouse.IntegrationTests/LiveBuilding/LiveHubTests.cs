using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class LiveHubTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string LiveMethod = "live";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Theory]
    [InlineData(HttpTransportType.ServerSentEvents)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task Joined_client_receives_site_update(HttpTransportType transport)
    {
        var ct = TestContext.Current.CancellationToken;
        var site = await SiteAsync("nordlys", "CPH-HQ");
        await using var hub = Connect(DemoUser.Jonas.Subject, DemoUser.Jonas.Email, "nordlys", transport);
        var received = new TaskCompletionSource<LiveUpdateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<LiveUpdateDto>(LiveMethod, u => received.TrySetResult(u));
        await hub.StartAsync(ct);
        await hub.InvokeAsync("JoinSite", site, ct);

        var update = Update(site);
        await PublishAsync("nordlys", update);

        var got = await received.Task.WaitAsync(Wait, ct);
        Assert.Equal(LiveUpdateKind.DoorState, got.Kind);
        Assert.Equal(update.DoorState!.DoorId, got.DoorState!.DoorId);
        Assert.Equal(DoorState.Open, got.DoorState.State);
    }

    [Fact]
    public async Task Other_tenant_update_is_not_received()
    {
        var ct = TestContext.Current.CancellationToken;
        var site = await SiteAsync("nordlys", "CPH-HQ");
        await using var hub = Connect(DemoUser.Jonas.Subject, DemoUser.Jonas.Email, "nordlys", HttpTransportType.LongPolling);
        var received = new List<LiveUpdateDto>();
        var nordlysArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var own = Update(site);
        hub.On<LiveUpdateDto>(LiveMethod, u =>
        {
            lock (received)
            {
                received.Add(u);
            }

            if (u.DoorState?.DoorId == own.DoorState!.DoorId)
            {
                nordlysArrived.TrySetResult();
            }
        });
        await hub.StartAsync(ct);
        await hub.InvokeAsync("JoinSite", site, ct);

        await PublishAsync("bricks", Update(site));
        await PublishAsync("nordlys", own);

        await nordlysArrived.Task.WaitAsync(Wait, ct);
        lock (received)
        {
            Assert.Equal(own.DoorState!.DoorId, Assert.Single(received).DoorState!.DoorId);
        }
    }

    [Fact]
    public async Task Site_scoped_staff_cannot_join_other_site()
    {
        var ct = TestContext.Current.CancellationToken;
        var hq = await SiteAsync("nordlys", "CPH-HQ");
        var warehouse = await SiteAsync("nordlys", "AAR-WH");
        RoleAssignmentId assignmentId;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = await db.Persons.SingleAsync(p => p.Email == "person-01@nordlys.test", ct);
            var assignment = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, new Domain.Sites.SiteId(warehouse)).Value;
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            assignmentId = assignment.Id;
        }

        try
        {
            await using var hub = Connect("test-person-01", "person-01@nordlys.test", "nordlys", HttpTransportType.LongPolling);
            await hub.StartAsync(ct);

            await hub.InvokeAsync("JoinSite", warehouse, ct);
            var denied = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinSite", hq, ct));
            Assert.Contains("auth.forbidden", denied.Message, StringComparison.Ordinal);
        }
        finally
        {
            await using var scope = await _api.CreateTenantScopeAsync("nordlys");
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.RoleAssignments.Where(r => r.Id == assignmentId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Requester_cannot_connect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var hub = Connect(DemoUser.Anna.Subject, DemoUser.Anna.Email, "nordlys", HttpTransportType.LongPolling);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync(ct));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    private static LiveUpdateDto Update(Guid site)
    {
        var now = DateTimeOffset.UtcNow;
        return new LiveUpdateDto(LiveUpdateKind.DoorState, site, DoorState: new DoorStateDto(Guid.CreateVersion7(), DoorState.Open, now, now));
    }

    private HubConnection Connect(string sub, string email, string tenant, HttpTransportType transport) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, "api/v1/live/hub"), transport, o =>
            {
                o.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                o.Headers[TestAuthHandler.SubHeader] = sub;
                o.Headers[TestAuthHandler.EmailHeader] = email;
                o.Headers[TestAuthHandler.TenantHeader] = tenant;
            })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .Build();

    private async Task PublishAsync(string tenant, LiveUpdateDto update)
    {
        await using var scope = await _api.CreateTenantScopeAsync(tenant);
        await scope.ServiceProvider.GetRequiredService<ILiveUpdatePublisher>().PublishAsync(update, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SiteAsync(string tenant, string code)
    {
        await using var scope = await _api.CreateTenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == code, TestContext.Current.CancellationToken)).Id.Value;
    }
}
