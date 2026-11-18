using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record AreaResource(AreaId AreaId, SiteId SiteId, IReadOnlySet<PersonId> OwnerIds);
