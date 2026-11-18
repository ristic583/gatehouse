using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.UnitTests.Connectors;

public sealed class ConnectorRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static ConnectorRegistration New() =>
        ConnectorRegistration.Create(new TenantId("nordlys"), PacsInstanceId.New(), "HQ", "hash", Now);

    [Fact]
    public void Enroll_with_wrong_token_fails()
    {
        var r = New().Enroll("other", "thumb", Now.AddDays(90), Now);
        Assert.Equal("connector.enrollment_invalid", r.Error.Code);
    }

    [Fact]
    public void Enroll_after_24h_expires()
    {
        var r = New().Enroll("hash", "thumb", Now.AddDays(90), Now.AddHours(24).AddSeconds(1));
        Assert.Equal("connector.enrollment_expired", r.Error.Code);
    }

    [Fact]
    public void Enroll_activates_and_clears_token()
    {
        var c = New();
        Assert.True(c.Enroll("hash", "thumb", Now.AddDays(90), Now).IsSuccess);
        Assert.Equal(ConnectorStatus.Active, c.Status);
        Assert.Null(c.EnrollmentTokenHash);
        Assert.Equal("thumb", c.CertificateThumbprint);
        Assert.Equal("connector.enrollment_invalid", c.Enroll("hash", "t2", Now.AddDays(90), Now).Error.Code);
    }
}
