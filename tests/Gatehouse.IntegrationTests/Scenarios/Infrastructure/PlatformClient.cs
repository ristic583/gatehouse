namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>The platform operator (realm <c>gatehouse-platform</c>, password + TOTP from the AppHost parameters) signed in through the BFF.</summary>
public static class PlatformClient
{
    public const string OperatorEmail = "operator@platform.test";

    public static async Task<ScenarioClient> SignInAsync(ScenarioAppHostFixture fixture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var password = await fixture.ParameterAsync("platform-operator-password", ct);
        var otpSecret = await fixture.ParameterAsync("platform-operator-otp-secret", ct);
        var client = new ScenarioClient(fixture.WebUrl, OperatorEmail, password);
        try
        {
            await client.SignInPlatformAsync(otpSecret, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
