using System.Text.Json;
using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.UnitTests.Connectors;

public sealed class PacsCapabilityGapsTests
{
    private static string Json(PacsCapabilities c) => JsonSerializer.Serialize(c, ConnectorJson.Options);

    [Fact]
    public void Null_json_is_unknown()
    {
        Assert.Null(PacsCapabilityGaps.From(null));
        Assert.True(PacsCapabilityGaps.Supports(null, DoorCommandKind.Lockdown));
    }

    [Fact]
    public void Cloudpacs_capabilities_give_native_validity_and_lockdown_gaps()
    {
        var json = Json(new PacsCapabilities("CloudPacs", "1.0.0", false, true, true, true, true, [DoorCommandKind.MomentaryUnlock], 100));

        Assert.Equal([PacsCapabilityGaps.NativeValidity, PacsCapabilityGaps.Lockdown], PacsCapabilityGaps.From(json));
        Assert.True(PacsCapabilityGaps.Supports(json, DoorCommandKind.MomentaryUnlock));
        Assert.False(PacsCapabilityGaps.Supports(json, DoorCommandKind.Lockdown));
    }

    [Fact]
    public void Push_events_false_is_not_a_gap()
    {
        var json = Json(new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [DoorCommandKind.MomentaryUnlock, DoorCommandKind.Lockdown], 50));

        Assert.Equal([PacsCapabilityGaps.PinWrite, PacsCapabilityGaps.PhotoWrite], PacsCapabilityGaps.From(json));
    }
}
