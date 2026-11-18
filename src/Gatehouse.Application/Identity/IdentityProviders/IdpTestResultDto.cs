namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record IdpTestResultDto(bool Ok, IReadOnlyList<IdpTestCheckDto> Checks);
