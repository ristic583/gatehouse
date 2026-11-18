using System.Formats.Asn1;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gatehouse.ConnectorGateway;

/// <summary>mTLS authentication (connector-protocol.md §1): chain to the connector CA, registration by thumbprint, scope check.</summary>
public sealed class ConnectorCertificateAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CatalogDbContext catalog,
    ConnectorCertificateAuthority authority,
    ITenantConnectionResolver tenants,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ConnectorCertificate";
    public const string ErrorItemKey = "connector.auth.error";

    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ct = Context.RequestAborted;
        var cert = Context.Connection.ClientCertificate;
        if (cert is null)
        {
            return Failure("connector.unauthenticated", 401, "A client certificate is required.");
        }

        using var caCert = X509Certificate2.CreateFromPem(await authority.GetCaCertificateAsync(ct));
        if (!ChainsToCa(cert, caCert, time.GetUtcNow()))
        {
            return Failure("connector.unauthenticated", 401, "The client certificate is not trusted.");
        }

        var thumbprint = Convert.ToHexString(cert.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)).ToLowerInvariant();
        var registration = await catalog.ConnectorRegistrations.SingleOrDefaultAsync(r => r.CertificateThumbprint == thumbprint, ct);
        if (registration is null)
        {
            return Failure("connector.unauthenticated", 401, "The client certificate is not registered.");
        }

        if (registration.Status == ConnectorStatus.Revoked)
        {
            return Failure("connector.revoked", 403, "The connector registration is revoked.");
        }

        // M10: a suspended (or not yet provisioned) tenant's connectors are refused on every request; read uncached.
        var tenantId = registration.TenantId;
        var tenantStatus = await catalog.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantStatus?)t.Status)
            .SingleOrDefaultAsync(ct);
        if (tenantStatus != TenantStatus.Active)
        {
            return Failure("connector.tenant_inactive", 403, "The tenant of this connector is not active.");
        }

        var expectedSan = $"urn:gatehouse:tenant:{registration.TenantId.Value}"
            + $":pacs:{registration.PacsInstanceId.Value}:connector:{registration.Id.Value}";
        if (registration.Status != ConnectorStatus.Active || !CertificateUris(cert).Contains(expectedSan, StringComparer.Ordinal))
        {
            return Failure("connector.scope_mismatch", 403, "The certificate scope does not match the connector registration.");
        }

        var connectionString = await tenants.ResolveAsync(registration.TenantId, ct);
        if (connectionString.IsFailure)
        {
            return Failure("connector.scope_mismatch", 403, "The tenant of this connector is not available.");
        }

        Context.RequestServices.GetRequiredService<ITenantContextSetter>().Set(registration.TenantId, connectionString.Value);
        Context.RequestServices.GetRequiredService<ConnectorContext>()
            .Set(registration.TenantId, registration.PacsInstanceId, registration.Id);

        var now = time.GetUtcNow();
        if (registration.LastSeenAt is not { } seen || now - seen >= TouchInterval)
        {
            registration.Touch(now);
            await catalog.SaveChangesAsync(ct);
        }

        var identity = new ClaimsIdentity([new Claim("connector_id", registration.Id.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var (code, status, detail) = Context.Items[ErrorItemKey] as (string, int, string)?
            ?? ("connector.unauthenticated", 401, "A client certificate is required.");
        return ConnectorProblems.Create(
            Context, code, status, code == "connector.revoked" ? "Connector revoked" : "Authentication failed", detail)
            .ExecuteAsync(Context);
    }

    private static bool ChainsToCa(X509Certificate2 cert, X509Certificate2 ca, DateTimeOffset now)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        return chain.Build(cert);
    }

    private static List<string> CertificateUris(X509Certificate2 cert)
    {
        var uris = new List<string>();
        foreach (var ext in cert.Extensions.Where(e => e.Oid?.Value == "2.5.29.17"))
        {
            var seq = new AsnReader(ext.RawData, AsnEncodingRules.DER).ReadSequence();
            while (seq.HasData)
            {
                var tag = seq.PeekTag();
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 6)
                {
                    uris.Add(seq.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6)));
                }
                else
                {
                    seq.ReadEncodedValue();
                }
            }
        }

        return uris;
    }

    private AuthenticateResult Failure(string code, int status, string detail)
    {
        Context.Items[ErrorItemKey] = (code, status, detail);
        return AuthenticateResult.Fail(code);
    }
}
