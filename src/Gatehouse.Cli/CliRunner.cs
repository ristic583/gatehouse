using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gatehouse.Cli;

public sealed class CliRunner(IServiceProvider services, TextWriter output)
{
    public const int Success = 0;
    public const int OperationFailed = 1;
    public const int UsageError = 2;

    private static readonly (string Id, string Name, IdpKind Idp)[] DemoTenants =
    [
        ("nordlys", "Nordlys A/S", IdpKind.Oidc),
        ("bricks", "Bricks GmbH", IdpKind.Saml),
    ];

    public async Task<int> RunAsync(CliCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command is CliCommand.PurgeTestTenants purge)
        {
            // Needs no master key: only catalog ids and database names (MigrationRunner below resolves the secret protector).
            return await new TestTenantPurge(
                services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>(), output)
                .RunAsync(purge.Apply, ct);
        }

        var migrations = services.GetRequiredService<MigrationRunner>();
        switch (command)
        {
            case CliCommand.Help:
                await output.WriteLineAsync(CliArguments.Usage);
                return Success;

            case CliCommand.MigrateAll:
                return Report(await migrations.MigrateAllAsync(ct));

            case CliCommand.MigrateTenant migrate:
                await migrations.MigrateCatalogAsync(ct);
                return Report([await migrations.MigrateTenantAsync(migrate.TenantId, ct)]);

            case CliCommand.CreateTenant create:
                {
                    await migrations.MigrateCatalogAsync(ct);
                    await using var scope = services.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<TenantProvisioningService>()
                        .CreateTenantAsync(create.TenantId, create.DisplayName, create.IdpKind, ct);
                    await output.WriteLineAsync(result.IsSuccess
                        ? $"{create.TenantId}: created"
                        : $"{create.TenantId}: FAILED {result.Error.Code} {result.Error.Message}");
                    return result.IsSuccess ? Success : OperationFailed;
                }

            case CliCommand.Seed seed:
                {
                    var seeder = services.GetRequiredService<DemoSeeder>();
                    if (DemoSeeder.DefinitionFor(seed.TenantId) is null)
                    {
                        await output.WriteLineAsync($"{seed.TenantId}: FAILED no demo seed exists for this tenant");
                        return OperationFailed;
                    }

                    if (seed.ResetAccess)
                    {
                        await seeder.ResetAccessDataAsync(seed.TenantId, null, ct);
                        await output.WriteLineAsync($"{seed.TenantId}: access data reset");
                    }
                    else
                    {
                        await seeder.SeedAsync(seed.TenantId, ct);
                        await output.WriteLineAsync($"{seed.TenantId}: seeded");
                    }

                    return Success;
                }

            case CliCommand.ConnectorCreate connector:
                {
                    await using var scope = services.CreateAsyncScope();
                    var created = await scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>()
                        .CreateAsync(connector.TenantId, connector.PacsName, ct);
                    if (created.IsFailure)
                    {
                        await output.WriteLineAsync($"FAILED {created.Error.Code} {created.Error.Message}");
                        return OperationFailed;
                    }

                    await output.WriteLineAsync("Enrollment token (shown once, valid 24 h):");
                    await output.WriteLineAsync(created.Value);
                    return Success;
                }

            case CliCommand.RepairPendingDuplicates repair:
                return await new PendingDuplicateRepair(services, output).RunAsync(repair.TenantId, repair.Apply, ct);

            case CliCommand.Bootstrap bootstrap:
                return await BootstrapAsync(migrations, bootstrap.Demo, ct);

            default:
                throw new InvalidOperationException($"Unhandled command {command.GetType().Name}.");
        }
    }

    private async Task<int> BootstrapAsync(MigrationRunner migrations, bool demo, CancellationToken ct)
    {
        await migrations.MigrateCatalogAsync(ct);

        var failed = false;
        foreach (var (id, name, idp) in demo ? DemoTenants : [])
        {
            await using var scope = services.CreateAsyncScope();
            var created = await scope.ServiceProvider.GetRequiredService<TenantProvisioningService>()
                .CreateTenantIfMissingAsync(new TenantId(id), name, idp, ct);
            if (created.IsFailure)
            {
                failed = true;
                await output.WriteLineAsync($"{id}: FAILED {created.Error.Code} {created.Error.Message}");
            }
            else if (created.Value)
            {
                await output.WriteLineAsync($"{id}: created");
            }

            // The M10_PlatformCatalog backfill only touches tenants that existed at migration time;
            // a fresh catalog gets the demo email domains here.
            if (created.IsSuccess && DemoSeeder.DefinitionFor(new TenantId(id)) is { } seed)
            {
                await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants
                    .Where(t => t.Id == new TenantId(id) && t.EmailDomain == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.EmailDomain, seed.EmailDomain), ct);
            }
        }

        if (demo && !await EnsureDemoOrganizationsAsync(ct))
        {
            failed = true;
        }

        if (services.GetService<IConfiguration>()?[PlatformOperatorBootstrapper.EmailSetting] is { Length: > 0 } operatorEmail)
        {
            await using var platformScope = services.CreateAsyncScope();
            if (await platformScope.ServiceProvider.GetRequiredService<PlatformOperatorBootstrapper>().EnsureAsync(operatorEmail, ct))
            {
                await output.WriteLineAsync("platform operator: created");
            }
        }

        if (Report(await migrations.MigrateAllAsync(ct)) != Success || failed)
        {
            return OperationFailed;
        }

        var seeder = services.GetRequiredService<DemoSeeder>();
        foreach (var (id, _, _) in demo ? DemoTenants : [])
        {
            await seeder.SeedAsync(new TenantId(id), ct);
            await output.WriteLineAsync($"{id}: seeded");
        }

        await using var connectorScope = services.CreateAsyncScope();
        await connectorScope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>().EnsureCreatedAsync(ct);
        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var registrations = connectorScope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>();
        var demoConnectors = new[]
        {
            ("Connector:DemoToken", ConnectorRegistrationService.DemoPacs),
            ("Connector:DemoTokenAarhus", ConnectorRegistrationService.DemoPacsAarhus),
        };
        foreach (var (key, pacsName) in demoConnectors)
        {
            if (configuration[key] is { } demoToken && !string.IsNullOrWhiteSpace(demoToken))
            {
                await registrations.EnsureDemoConnectorAsync(demoToken, pacsName, ct);
            }
        }

        return Success;
    }

    /// <summary>Demo tenants get their Keycloak organization like platform-created ones (suspension needs it).</summary>
    private async Task<bool> EnsureDemoOrganizationsAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<DemoOrganizationSeeder>();
        if (!organizations.IsConfigured)
        {
            await output.WriteLineAsync("organizations: skipped (Keycloak:ProvisionerClientSecret not set)");
            return true;
        }

        var ok = true;
        foreach (var (id, name, _) in DemoTenants)
        {
            var domain = DemoSeeder.DefinitionFor(new TenantId(id))!.EmailDomain;
            var linked = await organizations.EnsureAsync(new TenantId(id), name, domain, ct);
            ok &= linked.IsSuccess;
            await output.WriteLineAsync(linked.IsSuccess
                ? $"{id}: organization linked"
                : $"{id}: FAILED organization {linked.Error.Code} {linked.Error.Message}");
        }

        return ok;
    }

    private int Report(IReadOnlyList<TenantMigrationResult> results)
    {
        var blockedByDuplicates = false;
        foreach (var result in results)
        {
            if (result.Succeeded)
            {
                output.WriteLine($"{result.TenantId}: migrated");
            }
            else if (PendingDuplicateRepair.IsUniqueIndexViolation(result.Error))
            {
                // The PendingRequestUnique index cannot be built while duplicates exist: point at the repair, not a stack trace.
                blockedByDuplicates = true;
                output.WriteLine($"{result.TenantId}: FAILED duplicate pending requests for the same person and area block "
                    + $"{PendingDuplicateRepair.UniqueIndex}. Review them with "
                    + $"'gatehouse-cli repair-pending-duplicates --tenant {result.TenantId}', "
                    + "add --apply to cancel all but the oldest, then migrate again.");
            }
            else
            {
                output.WriteLine($"{result.TenantId}: FAILED {result.Error}");
            }
        }

        return results.All(r => r.Succeeded) ? Success : blockedByDuplicates ? UsageError : OperationFailed;
    }
}
