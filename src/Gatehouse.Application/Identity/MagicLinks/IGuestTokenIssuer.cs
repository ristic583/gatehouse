using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary>Issues the platform's 30-minute guest JWT (HS256, iss <c>gatehouse-guest</c>, aud <c>gatehouse-api</c>).</summary>
public interface IGuestTokenIssuer
{
    public GuestToken Issue(Person guest, TenantId tenantId, DateTimeOffset now);
}
