using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record EntitlementResource(EntitlementId EntitlementId, PersonResource Subject, PersonId RequestedById, AreaResource Area);
