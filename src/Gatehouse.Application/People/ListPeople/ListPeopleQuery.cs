namespace Gatehouse.Application.People.ListPeople;

public sealed record ListPeopleQuery(string? Query, string? Type, string? Status, int? Limit);
