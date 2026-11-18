using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.UnitTests.Connectors;

public sealed class ConnectorAdapterTypesTests
{
    [Fact]
    public void Sensitive_pin_to_string_is_masked() => Assert.Equal("****", new SensitivePin("1234").ToString());

    [Fact]
    public void Card_number_mask_keeps_last_four()
    {
        Assert.Equal("****0300", CardNumberMask.Mask("100200300"));
        Assert.Equal("****", CardNumberMask.Mask("12"));
    }

    [Fact]
    public void Unsupported_result_has_capability_missing_code()
    {
        var r = AdapterResult.Unsupported("PIN write");
        Assert.Equal(AdapterStatus.Unsupported, r.Status);
        Assert.Equal("adapter.capability_missing", r.Error!.Code);
    }
}
