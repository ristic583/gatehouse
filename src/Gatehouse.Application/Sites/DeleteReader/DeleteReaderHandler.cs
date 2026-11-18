using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.DeleteReader;

/// <summary><c>DELETE /api/v1/admin/readers/{readerId}</c>; audit <c>reader.deleted</c>.</summary>
public sealed class DeleteReaderHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result> HandleAsync(DeleteReaderCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reader = await db.Readers.SingleOrDefaultAsync(r => r.Id == new ReaderId(command.ReaderId), ct);
        if (reader is null)
        {
            return SiteErrors.ReaderNotFound;
        }

        db.Readers.Remove(reader);
        await audit.RecordAsync("reader.deleted", "Reader", command.ReaderId, new { name = reader.Name }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
