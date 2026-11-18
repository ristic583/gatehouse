namespace Gatehouse.Application.People.Dtos;

public sealed record PeopleImportErrorDto(int Line, string Code, string? Field);
