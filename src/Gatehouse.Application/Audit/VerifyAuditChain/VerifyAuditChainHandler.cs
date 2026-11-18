using Gatehouse.Application.Audit.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Audit.VerifyAuditChain;

public sealed class VerifyAuditChainHandler(ITenantDb db, TimeProvider time)
{
    private const int PageSize = 1000;

    public async Task<Result<AuditVerificationDto>> HandleAsync(VerifyAuditChainQuery query, CancellationToken ct)
    {
        long count = 0;
        long last = 0;
        var prevHash = AuditHasher.GenesisHash;
        while (true)
        {
            var page = await db.AuditEntries.AsNoTracking()
                .Where(e => e.Sequence > last)
                .OrderBy(e => e.Sequence)
                .Take(PageSize)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var e in page)
            {
                if (e.Sequence != last + 1 || e.PrevHash != prevHash || !e.HasValidHash())
                {
                    return Result.Success(new AuditVerificationDto(false, count, count == 0 ? null : last, e.Sequence, time.GetUtcNow()));
                }

                count++;
                last = e.Sequence;
                prevHash = e.Hash;
            }
        }

        return Result.Success(new AuditVerificationDto(true, count, count == 0 ? null : last, null, time.GetUtcNow()));
    }
}
