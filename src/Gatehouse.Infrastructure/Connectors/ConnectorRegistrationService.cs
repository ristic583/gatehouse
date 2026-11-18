using System.Security.Cryptography;
using System.Text;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Infrastructure.Connectors;

public sealed class ConnectorRegistrationService(
    CatalogDbContext catalog, ITenantScopeFactory scopes, ConnectorCertificateAuthority authority, TimeProvider time)
    : IConnectorRegistrar
{
    public const string DemoTenant = "nordlys";
    public const string DemoPacs = "HQ-Copenhagen";
    public const string DemoPacsAarhus = "Warehouse-Aarhus";

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>CLI: creates a registration for the instance named <paramref name="pacsName"/> and returns the one-time enrollment
    /// token.</summary>
    public async Task<Result<string>> CreateAsync(TenantId tenantId, string pacsName, CancellationToken ct)
    {
        PacsInstanceId pacsId;
        await using (var scope = await scopes.CreateScopeAsync(tenantId, ct))
        {
            var pacs = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.Name == pacsName, ct);
            if (pacs is null)
            {
                return Error.NotFound("connector.not_found", "PACS instance not found.");
            }

            pacsId = pacs.Id;
        }

        var created = await CreateForInstanceAsync(tenantId, pacsId, ct);
        if (created.IsFailure)
        {
            return created.Error;
        }

        await using (var scope = await scopes.CreateScopeAsync(tenantId, ct))
        {
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                AuditActor.ForSystem("cli"), "connector.registered", "Connector", created.Value.ConnectorId
                    .ToString(), new { pacsInstanceId = pacsId.Value, pacsName });
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(ct);
        }

        return created.Value.EnrollmentToken;
    }

    public async Task<Result<ConnectorTicket>> CreateForInstanceAsync(
        TenantId tenantId, PacsInstanceId pacsInstanceId, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.SingleOrDefaultAsync(p => p.Id == pacsInstanceId, ct);
        if (pacs is null)
        {
            return Error.NotFound("pacs.not_found", "PACS instance not found.");
        }

        if (await catalog.ConnectorRegistrations.AnyAsync(r => r.TenantId == tenantId && r.PacsInstanceId == pacs.Id
            && r.Status != ConnectorStatus.Revoked, ct))
        {
            return Error.Conflict("connector.already_exists", "A connector registration already exists for this PACS instance.");
        }

        var token = "ghe_" + Base64Url(RandomNumberGenerator.GetBytes(32));
        var registration = ConnectorRegistration.Create(tenantId, pacs.Id, pacs.Name, HashToken(token), time.GetUtcNow());
        catalog.ConnectorRegistrations.Add(registration);
        pacs.SetFulfillmentMode(FulfillmentMode.Connector);
        await catalog.SaveChangesAsync(ct);
        await db.SaveChangesAsync(ct);
        return new ConnectorTicket(registration.Id.Value, token, registration.EnrollmentTokenExpiresAt!.Value);
    }

    public async Task<Result<Guid>> RevokeAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, DateTimeOffset now, CancellationToken ct)
    {
        var registration = await catalog.ConnectorRegistrations
            .Where(r => r.TenantId == tenantId && r.PacsInstanceId == pacsInstanceId && r.Status != ConnectorStatus.Revoked)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (registration is null)
        {
            return Error.NotFound("connector.not_found", "The PACS instance has no connector registration.");
        }

        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.SingleOrDefaultAsync(p => p.Id == pacsInstanceId, ct);
        registration.Revoke(now);
        pacs?.SetFulfillmentMode(FulfillmentMode.Manual);
        await catalog.SaveChangesAsync(ct);
        await db.SaveChangesAsync(ct);
        return registration.Id.Value;
    }

    public async Task<IReadOnlyList<ConnectorRegistrationInfo>> ListAsync(TenantId tenantId, CancellationToken ct) =>
        await catalog.ConnectorRegistrations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status != ConnectorStatus.Revoked)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ConnectorRegistrationInfo(
                r.Id.Value, r.PacsInstanceId.Value, r.Status, r.CreatedAt, r.EnrollmentTokenExpiresAt, r.CertificateNotAfter, r.LastSeenAt))
            .ToListAsync(ct);

    public Task<string> GetCaCertificatePemAsync(CancellationToken ct) => authority.GetCaCertificateAsync(ct);

    /// <summary>
    /// Development only: makes sure the demo connector for <paramref name="pacsName"/> (tenant nordlys) has a registration
    /// whose token is <paramref name="token"/>.
    /// </summary>
    public async Task EnsureDemoConnectorAsync(string token, string pacsName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pacsName);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var tenantId = new TenantId(DemoTenant);
        var hash = HashToken(token);
        await using (var scope = await scopes.CreateScopeAsync(tenantId, ct))
        {
            var pacs = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Name == pacsName, ct);
            if (pacs is null)
            {
                return;
            }

            var existing = await catalog.ConnectorRegistrations
                .Where(r => r.TenantId == tenantId && r.PacsInstanceId == pacs.Id && r.Status != ConnectorStatus.Revoked)
                .ToListAsync(ct);
            if (existing.Any(r => r.Status == ConnectorStatus.Active
                || (r.Status == ConnectorStatus.PendingEnrollment && r.EnrollmentTokenHash == hash)))
            {
                return;
            }

            foreach (var stale in existing)
            {
                stale.Revoke(time.GetUtcNow());
            }

            await catalog.SaveChangesAsync(ct);
        }

        await RegisterAsync(tenantId, pacsName, hash, "bootstrap", ct);
    }

    private async Task<Result> RegisterAsync(TenantId tenantId, string pacsName, string tokenHash, string actor, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.SingleOrDefaultAsync(p => p.Name == pacsName, ct);
        if (pacs is null)
        {
            return Error.NotFound("connector.not_found", "PACS instance not found.");
        }

        if (await catalog.ConnectorRegistrations.AnyAsync(r => r.TenantId == tenantId && r.PacsInstanceId == pacs.Id
            && r.Status != ConnectorStatus.Revoked, ct))
        {
            return Error.Conflict("connector.already_exists", "A connector registration already exists for this PACS instance.");
        }

        var now = time.GetUtcNow();
        var registration = ConnectorRegistration.Create(tenantId, pacs.Id, pacs.Name, tokenHash, now);
        catalog.ConnectorRegistrations.Add(registration);
        pacs.SetFulfillmentMode(FulfillmentMode.Connector);
        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
            AuditActor.ForSystem(actor),
            "connector.registered",
            "Connector",
            registration.Id.Value.ToString(),
            new { pacsInstanceId = pacs.Id.Value, pacsName = pacs.Name });
        await catalog.SaveChangesAsync(ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
