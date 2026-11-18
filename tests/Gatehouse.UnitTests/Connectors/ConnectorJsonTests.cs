using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.UnitTests.Connectors;

public sealed class ConnectorJsonTests
{
    [Fact]
    public void Poll_response_example_round_trips()
    {
        const string json = """
        {"serverTime":"2026-11-09T08:42:13.512Z","commands":[{"commandId":"01933b9a-1c2d-7e3f-8a4b-5c6d7e8f9a0b",
        "idempotencyKey":"GrantAccess:01933b99-aaaa-7bbb-8ccc-dddddddddddd:1","type":"GrantAccess","schemaVersion":1,
        "tenantId":"nordlys","pacsInstanceId":"01933b8e-70aa-7d10-8c11-55e3a0b9e001",
        "partitionKey":"person:01933b90-1111-7222-8333-444444444444","sequence":12,"priority":"Normal",
        "issuedAt":"2026-11-09T08:42:12.900Z","leaseUntil":"2026-11-09T08:44:13.512Z",
        "payload":{"personRef":"01933b90-1111-7222-8333-444444444444","cardNumber":"100200300",
        "vendorAccessLevelRef":"AL-CPH-SERVER","validFrom":"2026-11-09T00:00:00Z","validTo":"2026-11-23T00:00:00Z"}}]}
        """;
        var r = JsonSerializer.Deserialize<PollResponse>(json, ConnectorJson.Options)!;
        var env = Assert.Single(r.Commands);
        Assert.Equal(12, env.Sequence);
        Assert.Equal("AL-CPH-SERVER", ConnectorJson.DeserializePayload<GrantAccess>(env).VendorAccessLevelRef);
        var again = JsonSerializer.Deserialize<PollResponse>(JsonSerializer.Serialize(r, ConnectorJson.Options), ConnectorJson.Options)!;
        Assert.Equal(env.IdempotencyKey, again.Commands[0].IdempotencyKey);
    }

    [Fact]
    public void Ack_request_example_round_trips()
    {
        const string json = """
        {"acks":[{"commandId":"01933b9a-1c2d-7e3f-8a4b-5c6d7e8f9a0b","idempotencyKey":"GrantAccess:x:1","status":"Applied","pacsRef":"inbox-row-88412","appliedAt":"2026-11-09T08:42:15.020Z","error":null},
        {"commandId":"01933b9a-2222-7e3f-8a4b-5c6d7e8f9a0c","idempotencyKey":"SetPin:y:1","status":"Unsupported","pacsRef":null,"appliedAt":null,
        "error":{"code":"adapter.capability_missing","message":"PIN write not supported","retryable":false}}]}
        """;
        var r = JsonSerializer.Deserialize<AckRequest>(json, ConnectorJson.Options)!;
        Assert.Equal(AckStatus.Applied, r.Acks[0].Status);
        Assert.Equal(AckStatus.Unsupported, r.Acks[1].Status);
        Assert.False(r.Acks[1].Error!.Retryable);
        var again = JsonSerializer.Deserialize<AckRequest>(JsonSerializer.Serialize(r, ConnectorJson.Options), ConnectorJson.Options)!;
        Assert.Equal(2, again.Acks.Count);
    }

    [Fact]
    public void Enums_serialize_as_strings()
    {
        Assert.Equal("\"Emergency\"", JsonSerializer.Serialize(RevocationKind.Emergency, ConnectorJson.Options));
        Assert.Equal("\"Unsupported\"", JsonSerializer.Serialize(AckStatus.Unsupported, ConnectorJson.Options));
    }
}
