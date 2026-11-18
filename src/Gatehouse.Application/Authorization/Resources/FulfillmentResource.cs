using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record FulfillmentResource(FulfillmentId FulfillmentId, SiteId SiteId, PersonId SubjectId, PersonId RequestedById);
