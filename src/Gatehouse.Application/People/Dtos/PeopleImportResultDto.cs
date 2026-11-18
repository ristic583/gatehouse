namespace Gatehouse.Application.People.Dtos;

public sealed record PeopleImportResultDto(
    bool DryRun, int Created, int Updated, int Unchanged, IReadOnlyList<PeopleImportErrorDto> Errors);
