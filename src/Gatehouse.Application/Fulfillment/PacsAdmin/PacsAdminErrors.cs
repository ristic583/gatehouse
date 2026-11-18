using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public static class PacsAdminErrors
{
    public static Error NotFound { get; } = Error.NotFound("pacs.not_found", "The PACS instance does not exist.");
}
