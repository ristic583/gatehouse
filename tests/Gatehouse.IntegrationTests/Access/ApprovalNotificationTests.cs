using Gatehouse.Application.Access.Notifications;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Domain.Access;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatehouse.IntegrationTests.Access;

public sealed class ApprovalNotificationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task New_request_emails_the_manager_with_a_review_link()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        var sender = new RecordingSender(enabled: true);

        var sent = await NotifyAsync(sender, request.EntitlementId, request.Steps[0].StepId, ct);

        Assert.Equal(1, sent);
        var mail = Assert.Single(sender.Sent);
        Assert.Equal(DemoUser.Mikkel.Email, mail.To);
        Assert.Contains("https://gh.test/approvals", mail.Body, StringComparison.Ordinal);
        Assert.Contains("Needed for the infrastructure project", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_when_email_is_off()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        var sender = new RecordingSender(enabled: false);

        Assert.Equal(0, await NotifyAsync(sender, request.EntitlementId, request.Steps[0].StepId, ct));
        Assert.Empty(sender.Sent);
    }

    private async Task<int> NotifyAsync(RecordingSender sender, Guid entitlementId, Guid stepId, CancellationToken ct)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlement = await db.Entitlements.AsNoTracking().SingleAsync(e => e.Id == new EntitlementId(entitlementId), ct);
        var step = entitlement.Steps.Single(s => s.Id == new ApprovalStepId(stepId));
        var handler = new NotifyApproversHandler(db, sender, Options.Create(new NotificationOptions { PublicUrl = "https://gh.test" }), NullLogger<NotifyApproversHandler>.Instance);
        return await handler.HandleAsync(new NotifyApproversCommand(entitlement.Id, step.Id, step.AssignedApproverId), ct);
    }

    private sealed class RecordingSender(bool enabled) : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public bool IsEnabled => enabled;

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
        {
            Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }
}
