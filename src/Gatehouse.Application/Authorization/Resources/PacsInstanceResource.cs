using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record PacsInstanceResource(PacsInstanceId PacsInstanceId, IReadOnlySet<SiteId> SiteIds);
