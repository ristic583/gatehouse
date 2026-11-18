using Gatehouse.Application.Audit;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Infrastructure.Connectors;

/// <summary>Exchanges a one-time enrollment token + CSR for a client certificate (connector-protocol.md §2.1).</summary>
public sealed class EnrollConnectorService(
    CatalogDbContext catalog,
    ConnectorCertificateAuthority authority,
    ITenantScopeFactory scopes,
    TimeProvider time)
{
    public async Task<Result<EnrollResponse>> EnrollAsync(EnrollRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hash = ConnectorRegistrationService.HashToken(request.EnrollmentToken);

        // PT-31: the token is single use. The row lock makes a parallel enrollment with the same token wait until this one
        // commits; it then finds no row (the hash is cleared) and gets connector.enrollment_invalid. A refused CSR rolls back,
        // so the token stays usable.
        await using var transaction = await catalog.Database.BeginTransactionAsync(ct);
        var registration = await catalog.ConnectorRegistrations
            .FromSql($"SELECT * FROM catalog.connector_registrations WHERE enrollment_token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (registration is null)
        {
            return Error.Unauthorized("connector.enrollment_invalid", "Enrollment token is invalid.");
        }

        var tenantId = registration.TenantId;
        var tenantStatus = await catalog.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantStatus?)t.Status)
            .SingleOrDefaultAsync(ct);
        if (tenantStatus != TenantStatus.Active)
        {
            return Error.Forbidden("connector.tenant_inactive", "The tenant of this connector is not active.");
        }

        var now = time.GetUtcNow();
        if (registration.EnrollmentTokenExpiresAt is { } exp && now > exp)
        {
            return Error.Unauthorized("connector.enrollment_expired", "Enrollment token has expired.");
        }

        var issued = await authority.IssueClientCertificateAsync(
            request.CsrPem, registration.TenantId, registration.PacsInstanceId, registration.Id, now, ct);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var enrolled = registration.Enroll(hash, issued.Value.ThumbprintSha256Hex, issued.Value.NotAfter, now);
        if (enrolled.IsFailure)
        {
            return enrolled.Error;
        }

        await using var scope = await scopes.CreateScopeAsync(registration.TenantId, ct);
        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
            AuditActor.ForAgent(registration.Id),
            "connector.enrolled",
            "Connector",
            registration.Id.Value.ToString(),
            new { pacsInstanceId = registration.PacsInstanceId.Value, agentVersion = request.AgentVersion });
        await catalog.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(ct);

        return new EnrollResponse(
            registration.Id.Value,
            registration.TenantId.Value,
            registration.PacsInstanceId.Value,
            issued.Value.CertificatePem,
            issued.Value.CaPem,
            issued.Value.NotAfter);
    }
}
