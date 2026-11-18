using Gatehouse.Application.Audit;
using Gatehouse.Application.Audit.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Fulfillment;

public sealed class FulfillmentDetailLoader(ITenantDb db, FulfillmentDtoBuilder builder)
{
    public async Task<FulfillmentDetailDto> LoadAsync(FulfillmentEntity fulfillment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);
        var (item, desired) = (await builder.BuildAsync([fulfillment], ct))[0];

        ConfirmedStateDto? confirmed = null;
        if (fulfillment.ConfirmedAt is { } at && fulfillment.ConfirmedByKind is { } kind && fulfillment.ConfirmedById is { } byId)
        {
            string? name = null;
            if (kind == ConfirmerKind.Staff && Guid.TryParse(byId, out var guid))
            {
                var pid = new PersonId(guid);
                name = await db.Persons.AsNoTracking()
                    .Where(p => p.Id == pid)
                    .Select(p => p.GivenName + " " + p.FamilyName)
                    .SingleOrDefaultAsync(ct);
            }

            confirmed = new ConfirmedStateDto(kind.ToString(), byId, name, at, fulfillment.PacsRef);
        }

        var subjectId = fulfillment.Id.Value.ToString();
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(e => e.SubjectType == AuditSubjects.Fulfillment && e.SubjectId == subjectId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
        var connector = await db.PacsInstances.AsNoTracking()
            .Where(p => p.Id == fulfillment.PacsInstanceId && p.FulfillmentMode == FulfillmentMode.Connector)
            .Select(p => new { p.CapabilitiesJson })
            .FirstOrDefaultAsync(ct);
        var managed = connector is not null;
        string? cmdStatus = null;
        int? cmdAttempts = null;
        DateTimeOffset? next = null;
        string? lastError = null;
        if (fulfillment.CommandId is { } commandId)
        {
            var cmd = await db.ConnectorCommands.AsNoTracking().Where(c => c.Id == commandId)
                .Select(c => new { c.Status, c.Attempts, c.NextAttemptAt, c.LastError }).SingleOrDefaultAsync(ct);
            if (cmd is not null)
            {
                (cmdStatus, cmdAttempts, next, lastError) = (cmd.Status.ToString(), cmd.Attempts, cmd.NextAttemptAt, cmd.LastError);
            }
        }

        return new FulfillmentDetailDto(item, desired, confirmed, await AuditEntryDtoMapper.MapAsync(
            entries, db, ct), managed, cmdStatus, cmdAttempts, next, lastError,
            managed ? PacsCapabilityGaps.From(connector!.CapabilitiesJson) : null);
    }
}
