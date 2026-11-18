using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// Template databases built once per run by <see cref="GatehouseApiFactory"/>: a migrated catalog with tenants <c>nordlys</c>,
/// <c>bricks</c> and suspended <c>frozen</c>, and their seeded tenant databases (demo access data reset, no identity links).
/// Every factory gets its own copy (<see cref="CloneAsync"/>), so no test sees another test's data.
/// </summary>
public sealed record DemoTemplate(string Catalog, IReadOnlyDictionary<string, string> Tenants)
{
    private const string Key = "demo";

    public IEnumerable<string> Databases => Tenants.Values.Append(Catalog);

    public static Task<DemoTemplate> GetAsync(PostgresFixture postgres) =>
        postgres.GetOrCreateTemplateAsync(Key, () => GatehouseApiFactory.BuildDemoTemplateAsync(postgres));

    public static DemoTemplate NewNames() => new(
        PostgresFixture.UniqueName("tpl_catalog"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nordlys"] = PostgresFixture.UniqueName("tpl_nordlys"),
            ["bricks"] = PostgresFixture.UniqueName("tpl_bricks"),
            ["frozen"] = PostgresFixture.UniqueName("tpl_frozen"),
        });

    /// <summary>
    /// Clones the catalog and every tenant database, re-points the cloned catalog at the cloned tenant databases (before any
    /// host reads it, so nothing caches a template connection string) and returns the catalog connection string.
    /// The created database names are added to <paramref name="databases"/> for the owner to drop.
    /// </summary>
    public async Task<string> CloneAsync(PostgresFixture postgres, ICollection<string> databases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(databases);
        var catalog = PostgresFixture.UniqueName("catalog");
        var tenants = Tenants.ToDictionary(t => t.Key, t => PostgresFixture.UniqueName("t_" + t.Key), StringComparer.Ordinal);
        foreach (var name in tenants.Values.Append(catalog))
        {
            databases.Add(name);
        }

        await Task.WhenAll(tenants.Select(t => postgres.CloneDatabaseAsync(Tenants[t.Key], t.Value, ct)).Append(postgres.CloneDatabaseAsync(Catalog, catalog, ct)));

        var protector = new AesGcmSecretProtector(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(AesGcmSecretProtector.MasterKeySetting, GatehouseApiFactory.MasterKey)])
            .Build());
        var catalogConnectionString = postgres.ConnectionStringFor(catalog);
        await using var connection = new NpgsqlConnection(catalogConnectionString);
        await connection.OpenAsync(ct);
        foreach (var (slug, database) in tenants)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE catalog.tenants SET encrypted_connection_string = @cs WHERE id = @id";
            update.Parameters.AddWithValue("cs", protector.Protect(postgres.ConnectionStringFor(database, TenantDatabaseProvisioner.MaxPoolSize)));
            update.Parameters.AddWithValue("id", slug);
            await update.ExecuteNonQueryAsync(ct);
        }

        return catalogConnectionString;
    }
}
