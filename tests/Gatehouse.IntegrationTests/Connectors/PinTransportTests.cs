using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Gatehouse.Application.Credentials;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

/// <summary>Gateway re-encryption of <c>SetPin</c> (M8-11); the test's commands and registrations are removed afterwards.</summary>
public sealed class PinTransportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);
    private readonly string _partition = $"person:{Guid.NewGuid()}";

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Polled_set_pin_is_encrypted_to_agent_certificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var vault = _factory.Services.GetRequiredService<IPinVault>();
        var (ciphertext, keyId) = await vault.EncryptAsync(Pin.Create("731905").Value, ct);
        var reference = vault.ProtectForTransport(ciphertext, keyId);
        var commandId = await SeedAsync(new SetPin("person-ref", "100200300", reference), ct);
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");

        using var client = _factory.CreateClient();
        using var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/poll", new PollRequest(50, 0), cert);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(ct);
        var poll = JsonSerializer.Deserialize<PollResponse>(raw, ConnectorJson.Options)!;

        var delivered = Assert.Single(poll.Commands, c => c.CommandId == commandId.Value);
        var payload = ConnectorJson.DeserializePayload<SetPin>(delivered);
        Assert.DoesNotContain("vault:", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("731905", raw, StringComparison.Ordinal);
        using var rsa = cert.GetRSAPrivateKey()!;
        Assert.Equal("731905", Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(payload.EncryptedPin), RSAEncryptionPadding.OaepSHA256)));
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var stored = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == commandId, ct);
        Assert.Contains(reference, stored.PayloadJson, StringComparison.Ordinal);
    }

    private async Task<CommandId> SeedAsync(SetPin payload, CancellationToken ct)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
        var command = Domain.Connectors.ConnectorCommand.Create(pacs.Id, ConnectorCommandTypes.SetPin, _partition, 1, CommandPriority.Normal,
            $"SetPin:{_partition}:1", JsonSerializer.Serialize(payload, ConnectorJson.Options), null, DateTimeOffset.UtcNow);
        db.ConnectorCommands.Add(command);
        await db.SaveChangesAsync(ct);
        return command.Id;
    }
}
