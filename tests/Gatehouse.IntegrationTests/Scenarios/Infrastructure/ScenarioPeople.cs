using System.Globalization;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Application.People.Dtos;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Creates a fresh employee with an active card per scenario so person-level state never collides between scenarios.</summary>
public static class ScenarioPeople
{
    /// <summary>The admin creates the employee and issues the card; the manager activates it (only the manager or the person may).</summary>
    public static async Task<ScenarioPerson> NewEmployeeWithCardAsync(ScenarioClient admin, ScenarioClient manager, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(manager);
        var managerId = await PersonIdAsync(admin, manager.Email, ct);
        var email = $"scenario-{Guid.NewGuid():N}@nordlys.test";
        var person = await admin.PostAsync<PersonSummaryDto>(
            "v1/admin/people/employees", new CreateEmployeeRequest(email, "Scenario", "Employee", managerId, "Europe/Copenhagen"), ct);
        var cardNumber = NewCardNumber();
        var card = await admin.PostAsync<CardDto>($"v1/people/{person.PersonId}/cards", new { cardNumber }, ct);
        var active = await manager.PostAsync<CardDto>($"v1/cards/{card.CardId}/activate", null, ct);
        if (active.State != "Active")
        {
            throw new InvalidOperationException($"Card of {email} is {active.State} after activation.");
        }

        return new ScenarioPerson(person.PersonId, email, card.CardId, cardNumber);
    }

    /// <summary>
    /// A sponsored external onboarded by the sponsor (<c>POST /people</c> with a card number, like tests/e2e/fixtures/lifecycle.ts
    /// <c>onboard</c>); the sponsor activates the card.
    /// </summary>
    public static async Task<ScenarioPerson> NewSponsoredExternalWithCardAsync(ScenarioClient sponsor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sponsor);
        var email = $"scenario-{Guid.NewGuid():N}@nordlys.test";
        var cardNumber = NewCardNumber();
        var start = DateTimeOffset.UtcNow.Date;
        var onboarded = await sponsor.PostAsync<System.Text.Json.JsonElement>("v1/people", new
        {
            type = "External",
            givenName = "Scenario",
            familyName = "External",
            email,
            start = new DateTimeOffset(start, TimeSpan.Zero),
            end = new DateTimeOffset(start.AddDays(60), TimeSpan.Zero),
            timeZoneId = "Europe/Copenhagen",
            sponsorId = (Guid?)null,
            cardNumber,
        }, ct);
        var personId = onboarded.GetProperty("summary").GetProperty("personId").GetGuid();
        var detail = await sponsor.GetAsync<PersonDetailDto>($"v1/people/{personId}", ct);
        var card = detail.Cards.Single();
        await sponsor.PostAsync<CardDto>($"v1/cards/{card.CardId}/activate", null, ct);
        return new ScenarioPerson(personId, email, card.CardId, cardNumber);
    }

    public static async Task<Guid> PersonIdAsync(ScenarioClient client, string email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        var people = await client.GetAsync<List<PersonSummaryDto>>($"v1/people?query={Uri.EscapeDataString(email)}", ct);
        return people.SingleOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase))?.PersonId
            ?? throw new InvalidOperationException($"No person with email {email}.");
    }

    /// <summary>16 digits starting 88 (6-20 digits allowed), unique per call.</summary>
    private static string NewCardNumber() =>
        "88" + ((BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) & long.MaxValue) % 100_000_000_000_000L).ToString("D14", CultureInfo.InvariantCulture);
}
