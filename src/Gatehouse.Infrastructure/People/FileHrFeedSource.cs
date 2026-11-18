using System.Text.Json;
using Gatehouse.Application.People.HrSync;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.People;

/// <summary>Reads <c>{HrSync:FeedDirectory}/{tenantId}.json</c> (camelCase array); the directory is read on every call.</summary>
public sealed class FileHrFeedSource(IConfiguration configuration) : IHrFeedSource
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<HrFeedRecord>?> ReadAsync(TenantId tenantId, CancellationToken ct)
    {
        var directory = configuration["HrSync:FeedDirectory"];
        if (string.IsNullOrWhiteSpace(directory) || tenantId.Value.Contains('/', StringComparison.Ordinal)
            || tenantId.Value.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(directory, tenantId.Value + ".json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<HrFeedRecord>>(stream, Options, ct) ?? [];
    }
}
