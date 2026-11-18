using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>Test stand-in for mTLS: header <c>X-Test-Client-Cert</c> (base64 DER) becomes the connection's client certificate.</summary>
public sealed class TestClientCertificateStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-Cert";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return app =>
        {
            app.Use(async (context, nxt) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var value) && !string.IsNullOrEmpty(value))
                {
                    context.Connection.ClientCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(value.ToString()));
                }

                await nxt();
            });
            next(app);
        };
    }
}
