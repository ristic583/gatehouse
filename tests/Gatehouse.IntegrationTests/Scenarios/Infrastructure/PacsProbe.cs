using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Reads what the simulated PACS hold and injects their failures (chaos is always cleared by disposing the returned handle).</summary>
public sealed class PacsProbe(HttpClient legacyPacs, HttpClient cloudPacs, NpgsqlDataSource legacyPacsDb)
{
    private const string CloudPacsApiKey = "dev-cloudpacs-key";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<IReadOnlyList<LegacyAssignment>> LegacyAssignmentsAsync(string cardNumber, CancellationToken ct) =>
        (await AllLegacyAssignmentsAsync(ct)).Where(a => a.CardNumber == cardNumber).ToList();

    public async Task<IReadOnlyList<LegacyAssignment>> AllLegacyAssignmentsAsync(CancellationToken ct) =>
        (await legacyPacs.GetFromJsonAsync<List<LegacyAssignment>>(new Uri("/assignments", UriKind.Relative), ScenarioJson.Options, ct))!;

    public async Task<IReadOnlyList<CloudAssignment>> CloudAssignmentsAsync(string cardNumber, CancellationToken ct)
    {
        var all = await cloudPacs.GetFromJsonAsync<List<CloudAssignment>>(new Uri("/dev/assignments", UriKind.Relative), ScenarioJson.Options, ct);
        return all!.Where(a => a.CardNumber == cardNumber).ToList();
    }

    /// <summary>LegacyPACS doors with their lockdown flag and momentary-unlock end (<c>GET /doors</c>).</summary>
    public async Task<IReadOnlyList<LegacyDoor>> LegacyDoorsAsync(CancellationToken ct) =>
        (await legacyPacs.GetFromJsonAsync<List<LegacyDoor>>(new Uri("/doors", UriKind.Relative), ScenarioJson.Options, ct))!;

    /// <summary>LegacyPACS card state (<c>Active</c>, <c>Suspended</c>, …) from its database; null when the card is unknown.</summary>
    public async Task<string?> LegacyCardStateAsync(string cardNumber, CancellationToken ct)
    {
        await using var command = legacyPacsDb.CreateCommand("SELECT state FROM cards WHERE card_number = $1");
        command.Parameters.AddWithValue(cardNumber);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>CloudPACS card state (<c>active</c>, …) through its vendor API; null when the card is unknown.</summary>
    public async Task<string?> CloudCardStateAsync(string cardNumber, CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var uri = "/api/cards?limit=200" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await CloudApiGetAsync(uri, ct);
            foreach (var card in page.GetProperty("items").EnumerateArray())
            {
                if (card.GetProperty("cardNumber").GetString() == cardNumber)
                {
                    return card.GetProperty("state").GetString();
                }
            }

            cursor = page.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }
        while (cursor is not null);

        return null;
    }

    /// <summary>
    /// Creates a CloudPACS membership behind Gatehouse's back (vendor API with the dev key): cardholder <paramref name="externalRef"/>
    /// with the card, member of <paramref name="groupId"/>.
    /// </summary>
    public async Task AddCloudLeftoverAsync(string externalRef, string cardNumber, string groupId, CancellationToken ct)
    {
        await CloudApiSendAsync(HttpMethod.Put, $"/api/cardholders/{externalRef}", new { givenName = "Scenario", familyName = "Leftover", cardNumber }, ct);
        await CloudApiSendAsync(HttpMethod.Put, $"/api/access-groups/{groupId}/members/{externalRef}", new { cardNumber }, ct);

        // CloudPACS lists are eventually consistent (ReadDelayMs): a reconciliation requested right away can export before the
        // membership is listed and find nothing. Return only once the vendor list (what the agent's export reads) shows it.
        var listed = await WaitUntilAsync(c => CloudAssignmentListedAsync(externalRef, groupId, c), TimeSpan.FromSeconds(30), ct);
        if (!listed)
        {
            throw new InvalidOperationException($"CloudPACS never listed the membership of {externalRef} in {groupId}.");
        }
    }

    private async Task<bool> CloudAssignmentListedAsync(string externalRef, string groupId, CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var uri = "/api/assignments?limit=200" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await CloudApiGetAsync(uri, ct);
            foreach (var a in page.GetProperty("items").EnumerateArray())
            {
                if (a.GetProperty("externalRef").GetString() == externalRef
                    && a.GetProperty("groupId").GetString() == groupId
                    && a.GetProperty("active").GetBoolean())
                {
                    return true;
                }
            }

            cursor = page.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }
        while (cursor is not null);

        return false;
    }

    public async Task<IAsyncDisposable> LegacyChaosAsync(int failNext, int failNextPermanent, int delayMs, CancellationToken ct)
    {
        using var response = await legacyPacs.PostAsJsonAsync(new Uri("/chaos", UriKind.Relative), new { failNext, failNextPermanent, delayMs }, ct);
        response.EnsureSuccessStatusCode();
        return new ChaosHandle(legacyPacs, "/chaos");
    }

    public async Task<IAsyncDisposable> CloudChaosAsync(int? failNext, int? rateLimitNext, double? errorRate, double? rateLimitRate, CancellationToken ct)
    {
        using var response = await cloudPacs.PostAsJsonAsync(new Uri("/dev/chaos", UriKind.Relative), new { failNext, rateLimitNext, errorRate, rateLimitRate }, ct);
        response.EnsureSuccessStatusCode();
        return new ChaosHandle(cloudPacs, "/dev/chaos");
    }

    /// <summary>Polls every 500 ms until the predicate holds; false on timeout.</summary>
    public static async Task<bool> WaitUntilAsync(Func<CancellationToken, Task<bool>> predicate, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            if (await predicate(ct))
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>The AppHost gives CloudPACS a small error rate: retry injected 503/429 answers.</summary>
    private async Task<JsonElement> CloudApiGetAsync(string uri, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, UriKind.Relative));
            request.Headers.Add("X-Api-Key", CloudPacsApiKey);
            using var response = await cloudPacs.SendAsync(request, ct);
            if (attempt < 5 && (int)response.StatusCode is 503 or 429)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ScenarioJson.Options, ct);
        }
    }

    private async Task CloudApiSendAsync(HttpMethod method, string uri, object body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative)) { Content = JsonContent.Create(body, options: ScenarioJson.Options) };
            request.Headers.Add("X-Api-Key", CloudPacsApiKey);
            using var response = await cloudPacs.SendAsync(request, ct);
            if (attempt < 5 && (int)response.StatusCode is 503 or 429)
            {
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{method} {uri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
            }

            return;
        }
    }

    private sealed class ChaosHandle(HttpClient client, string path) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var response = await client.DeleteAsync(new Uri(path, UriKind.Relative), CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }
    }
}
