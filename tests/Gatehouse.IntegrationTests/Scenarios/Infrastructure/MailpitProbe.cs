using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Dev mail catcher (Mailpit API v1, http://localhost:8025): the Keycloak action link of the newest mail to an address.</summary>
public static partial class MailpitProbe
{
    private static readonly Uri Api = new("http://localhost:8025/api/v1/");

    public static async Task<Uri> ActionLinkForAsync(string email, TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = Api };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"search?query={Uri.EscapeDataString($"to:\"{email}\"")}", ct);
            var newest = search.GetProperty("messages").EnumerateArray()
                .OrderByDescending(m => m.GetProperty("Created").GetString(), StringComparer.Ordinal)
                .Select(m => m.GetProperty("ID").GetString())
                .FirstOrDefault();
            if (newest is not null)
            {
                var message = await http.GetFromJsonAsync<JsonElement>($"message/{newest}", ct);
                var body = $"{message.GetProperty("HTML").GetString()}\n{message.GetProperty("Text").GetString()}";
                var link = ActionLink().Match(body);
                return link.Success
                    ? new Uri(WebUtility.HtmlDecode(link.Value))
                    : throw new InvalidOperationException($"No Keycloak action link in the mail to {email}.");
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"No mail to {email} in Mailpit within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    [GeneratedRegex("https?://[^\\s\"'<>]+/login-actions/action-token\\?[^\\s\"'<>]+")]
    private static partial Regex ActionLink();
}
