using Gatehouse.Api.Features.LiveBuilding;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.LiveBuilding;

/// <summary>GH-AUTH-02: a joined live feed is dropped once the user may no longer view the site.</summary>
public sealed class LiveSubscriptionRevalidatorTests
{
    private static readonly Guid SiteA = Guid.NewGuid();
    private static readonly Guid SiteB = Guid.NewGuid();

    [Fact]
    public async Task Membership_is_removed_after_access_is_withdrawn()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new LiveSubscriptionRegistry();
        registry.Track(new LiveSubscription("c1", "nordlys", "sub-1", "jonas@nordlys.test", SiteA));
        registry.Track(new LiveSubscription("c1", "nordlys", "sub-1", "jonas@nordlys.test", SiteB));
        var access = new FakeAccess { Allowed = { SiteA, SiteB } };
        var hub = new FakeHub();
        var revalidator = new LiveSubscriptionRevalidator(registry, access, hub, TimeProvider.System, NullLogger<LiveSubscriptionRevalidator>.Instance);

        Assert.Equal(0, await revalidator.RevalidateOnceAsync(ct));

        access.Allowed.Remove(SiteB);
        Assert.Equal(1, await revalidator.RevalidateOnceAsync(ct));

        Assert.Equal([("c1", LiveBuildingHub.SiteGroup("nordlys", SiteB))], hub.Removed);
        Assert.Equal(SiteA, Assert.Single(registry.Snapshot()).SiteId);
    }

    [Fact]
    public async Task Removed_connection_receives_revoked_notice_before_removal()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new LiveSubscriptionRegistry();
        registry.Track(new LiveSubscription("c1", "nordlys", "sub-1", null, SiteA));
        var hub = new FakeHub();
        var revalidator = new LiveSubscriptionRevalidator(
            registry, new FakeAccess(), hub, TimeProvider.System, NullLogger<LiveSubscriptionRevalidator>.Instance);

        Assert.Equal(1, await revalidator.RevalidateOnceAsync(ct));

        Assert.Equal(
            [
                $"send c1 {LiveSubscriptionRevalidator.RevokedMethod} {SiteA} auth.forbidden",
                $"remove c1 {LiveBuildingHub.SiteGroup("nordlys", SiteA)}",
            ],
            hub.Log);
    }

    [Fact]
    public async Task Failing_check_removes_membership()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new LiveSubscriptionRegistry();
        registry.Track(new LiveSubscription("c1", "nordlys", "sub-1", null, SiteA));
        var revalidator = new LiveSubscriptionRevalidator(registry, new FakeAccess { Throws = true }, new FakeHub(), TimeProvider.System, NullLogger<LiveSubscriptionRevalidator>.Instance);

        Assert.Equal(1, await revalidator.RevalidateOnceAsync(ct));
        Assert.Empty(registry.Snapshot());
    }

    private sealed class FakeHub : IHubContext<LiveBuildingHub>, IGroupManager
    {
        public List<(string ConnectionId, string Group)> Removed { get; } = [];

        public List<string> Log { get; } = [];

        public IHubClients Clients => new FakeClients(this);

        public IGroupManager Groups => this;

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            Log.Add($"remove {connectionId} {groupName}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClients(FakeHub hub) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => new FakeClient(hub, connectionId);

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Group(string groupName) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeClient(FakeHub hub, string connectionId) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            hub.Log.Add($"send {connectionId} {method} {string.Join(' ', args)}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccess : ILiveSiteAccessChecker
    {
        public HashSet<Guid> Allowed { get; } = [];

        public bool Throws { get; init; }

        public Task<bool> CanViewAsync(string tenant, string subject, string? email, Guid siteId, CancellationToken ct) =>
            Throws ? throw new InvalidOperationException("tenant.inactive") : Task.FromResult(Allowed.Contains(siteId));
    }
}
