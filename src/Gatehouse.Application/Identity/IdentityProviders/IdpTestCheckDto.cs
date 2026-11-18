namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record IdpTestCheckDto(string Name, bool Ok, string? Code, string? Detail);
