using Gatehouse.Domain.Common;
using Gatehouse.ServiceDefaults.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Gatehouse.UnitTests.Logging;

public sealed class LogRedactionTests
{
    [Fact]
    public void Sensitive_attribute_properties_are_masked()
    {
        var output = Render(log => log.Information("Card {@Card}", new CardHolder("Anna", "secret-value-123")));

        Assert.Contains("Anna", output, StringComparison.Ordinal);
        Assert.Contains("***", output, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value-123", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Well_known_secret_property_names_are_masked()
    {
        var output = Render(log => log.Information("Login {@Login}", new LoginPayload(
            "anna@nordlys.test",
            "hunter2-password",
            "eyJ-access",
            "eyJ-refresh",
            "eyJ-id",
            "client-secret-xyz",
            "Host=db;Password=pg-secret",
            "v1:encrypted")));

        Assert.Contains("anna@nordlys.test", output, StringComparison.Ordinal);
        foreach (var secret in new[] { "hunter2-password", "eyJ-access", "eyJ-refresh", "eyJ-id", "client-secret-xyz", "pg-secret", "v1:encrypted" })
        {
            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Client_secret_is_redacted()
    {
        var request = new Gatehouse.Application.Identity.IdentityProviders.SaveIdentityProviderRequest(
            Gatehouse.Domain.Tenancy.IdpKind.Oidc,
            "Acme SSO",
            new Gatehouse.Application.Identity.IdentityProviders.OidcSettingsRequest("https://idp.acme.example/.well-known/openid-configuration", "gatehouse-client", "oidc-client-secret-987", ["openid"]),
            null);

        var output = Render(log => log.Information("Saving {@Request}", request));

        Assert.Contains("gatehouse-client", output, StringComparison.Ordinal);
        Assert.DoesNotContain("oidc-client-secret-987", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrollment_token_is_redacted()
    {
        var dto = new Gatehouse.Application.Fulfillment.PacsAdmin.ConnectorEnrollmentDto(
            Guid.NewGuid(), "ghe_enrollment-token-secret-321", DateTimeOffset.UtcNow, "https://gateway.example", "-----BEGIN CERTIFICATE-----", Gatehouse.Domain.Fulfillment.PacsVendor.LegacyPacs);

        var output = Render(log => log.Information("Enrollment {@Enrollment}", dto));

        Assert.Contains("https://gateway.example", output, StringComparison.Ordinal);
        Assert.DoesNotContain("enrollment-token-secret-321", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fake_pin_never_appears_in_output()
    {
        var output = Render(log => log.Information("Credential {@Obj}", new PinHolder("person-1", "4711")));

        Assert.Contains("person-1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("4711", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Pin_never_appears_in_logs()
    {
        var pin = Gatehouse.Domain.Credentials.Pin.Create("80417").Value;
        var output = Render(log =>
        {
            log.Information("Pin {Pin} and {@PinObject}", pin, pin);
            log.Information("Request {@Request}", new Gatehouse.Application.Credentials.SetPin.SetPinRequest("80417"));
            log.Information("Payload {@Payload}", new Gatehouse.Connector.Contracts.Commands.SetPin("person-1", "1002003", "vault:QUJD:local-v1"));
            log.Information("Agent pin {Pin}", new Gatehouse.Connector.Contracts.Adapters.SensitivePin("80417"));
        });

        Assert.Contains("person-1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("80417", output, StringComparison.Ordinal);
    }

    private static string Render(Action<Serilog.ILogger> write)
    {
        var sink = new CollectingSink();
        using (var logger = new LoggerConfiguration().ConfigureGatehouseDefaults().WriteTo.Sink(sink).CreateLogger())
        {
            write(logger);
        }

        return string.Join(Environment.NewLine, sink.Events.Select(e =>
        {
            using var writer = new StringWriter();
            e.RenderMessage(writer, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var property in e.Properties)
            {
                writer.Write(' ');
                property.Value.Render(writer, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            }

            return writer.ToString();
        }));
    }

    private sealed record CardHolder(string Name, [property: Sensitive] string CardSecret);

    private sealed record PinHolder(string PersonId, string Pin);

    private sealed record LoginPayload(
        string Email,
        string Password,
        string AccessToken,
        string RefreshToken,
        string IdToken,
        string ClientSecret,
        string ConnectionString,
        string EncryptedConnectionString);

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
