namespace Gatehouse.Application.Access.Dtos;

public sealed record AreaRosterDto(AreaRefDto Area, bool CanRevoke, bool CanEmergencyRevoke, IReadOnlyList<AreaRosterEntryDto> Entries);
