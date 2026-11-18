using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Cli;

/// <summary>Hand-rolled argument parser for gatehouse-cli.</summary>
public static class CliArguments
{
    public const string UsageErrorCode = "cli.usage";

    public const string Usage = """
        Usage: gatehouse-cli <command>

        Commands:
          migrate --all                                   Migrate the catalog and every tenant database
          migrate --tenant <id>                           Migrate one tenant database
          tenant create --id <id> --name <display name> --idp <oidc|saml|local>
                                                          Create a tenant database and catalog entry
          seed --tenant <id> [--reset-access]             Seed demo data (or reset demo access requests) for a demo tenant
          connector create --tenant <id> --pacs <name>    Register a connector for a PACS instance and print its one-time token
          repair-pending-duplicates [--tenant <id>] [--apply]
                                                          List (and with --apply cancel all but the oldest of) duplicate
                                                          pending requests for the same person and area
          purge-test-tenants [--apply]                    Development only: list (and with --apply delete the catalog
                                                          rows and databases of) e2e-*/scn-* tenants on local Postgres
          bootstrap                                       Migrate, create and seed demo tenants nordlys and bricks if missing
          bootstrap --production                          Migrate, ensure the platform operator and the connector CA; no demo data
          help                                            Show this text

        Exit codes: 0 success, 1 operation failed, 2 usage error.
        """;

    public static Result<CliCommand> Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args switch
        {
            [] or ["help"] or ["--help"] or ["-h"] => new CliCommand.Help(),
            ["connector", "create", "--tenant", var id, "--pacs", var pacs] when !string.IsNullOrWhiteSpace(pacs) =>
                ParseTenantId(id).IsSuccess
                ? new CliCommand.ConnectorCreate(ParseTenantId(id).Value, pacs)
                : ParseTenantId(id).Error,
            ["bootstrap"] => new CliCommand.Bootstrap(),
            ["bootstrap", "--production"] => new CliCommand.Bootstrap(Demo: false),
            ["migrate", "--all"] => new CliCommand.MigrateAll(),
            ["migrate", "--tenant", var id] => ParseTenantId(id).IsSuccess
                ? new CliCommand.MigrateTenant(ParseTenantId(id).Value)
                : ParseTenantId(id).Error,
            ["tenant", "create", .. var rest] => ParseTenantCreate(rest),
            ["repair-pending-duplicates", .. var rest] => ParseRepairPendingDuplicates(rest),
            ["purge-test-tenants"] => new CliCommand.PurgeTestTenants(Apply: false),
            ["purge-test-tenants", "--apply"] => new CliCommand.PurgeTestTenants(Apply: true),
            ["seed", "--tenant", var id] => ParseSeed(id, resetAccess: false),
            ["seed", "--tenant", var id, "--reset-access"] => ParseSeed(id, resetAccess: true),
            _ => UsageError($"Unknown command: {string.Join(' ', args)}"),
        };
    }

    private static Result<CliCommand> ParseTenantCreate(string[] options)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Length; i += 2)
        {
            if (i + 1 >= options.Length || options[i] is not ("--id" or "--name" or "--idp"))
            {
                return UsageError($"Unexpected option '{options[i]}' for 'tenant create'.");
            }

            values[options[i]] = options[i + 1];
        }

        if (!values.TryGetValue("--id", out var rawId))
        {
            return UsageError("'tenant create' requires --id.");
        }

        if (!values.TryGetValue("--name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return UsageError("'tenant create' requires --name.");
        }

        if (!values.TryGetValue("--idp", out var rawIdp) || !TryParseIdp(rawIdp, out var idp))
        {
            return UsageError("'tenant create' requires --idp oidc|saml|local.");
        }

        var id = ParseTenantId(rawId);
        return id.IsSuccess ? new CliCommand.CreateTenant(id.Value, name, idp) : id.Error;
    }

    private static Result<CliCommand> ParseRepairPendingDuplicates(string[] options)
    {
        TenantId? tenantId = null;
        var apply = false;
        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--apply":
                    apply = true;
                    break;
                case "--tenant" when i + 1 < options.Length && tenantId is null:
                    var id = ParseTenantId(options[++i]);
                    if (id.IsFailure)
                    {
                        return id.Error;
                    }

                    tenantId = id.Value;
                    break;
                default:
                    return UsageError($"Unexpected option '{options[i]}' for 'repair-pending-duplicates'.");
            }
        }

        return new CliCommand.RepairPendingDuplicates(tenantId, apply);
    }

    private static Result<CliCommand> ParseSeed(string rawId, bool resetAccess)
    {
        var id = ParseTenantId(rawId);
        return id.IsSuccess ? new CliCommand.Seed(id.Value, resetAccess) : id.Error;
    }

    private static Result<TenantId> ParseTenantId(string raw)
    {
        var id = TenantId.Create(raw);
        return id.IsSuccess ? id : Error.Validation(UsageErrorCode, id.Error.Message);
    }

    private static bool TryParseIdp(string raw, out IdpKind idp)
    {
        switch (raw)
        {
            case "oidc":
                idp = IdpKind.Oidc;
                return true;
            case "saml":
                idp = IdpKind.Saml;
                return true;
            case "local":
                idp = IdpKind.Local;
                return true;
            default:
                idp = default;
                return false;
        }
    }

    private static Error UsageError(string message) => Error.Validation(UsageErrorCode, message);
}
