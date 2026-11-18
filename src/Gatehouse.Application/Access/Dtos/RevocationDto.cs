using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Access.Dtos;

public sealed record RevocationDto(PersonRefDto? RevokedBy, DateTimeOffset RevokedAt, string Reason, string Kind);
