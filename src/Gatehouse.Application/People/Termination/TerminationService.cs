using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Termination;

/// <summary>
/// Terminates a person: every <c>Approved</c> entitlement is revoked (kind <c>Termination</c>) with high-priority revoke fulfillments,
/// every card
/// that is not Revoked is revoked with a <c>SuspendCard(Revoked)</c> per target. <c>PendingApproval</c> requests stay (follow-up). Does not
/// save;
/// after saving, the caller calls <see cref="NotifyAsync"/>.
/// </summary>
public sealed class TerminationService(ITenantDb db, FulfillmentPlanner planner, CardCommandService cardCommands, IAuditLog audit)
{
    public const string Reason = "Eligibility ended";
    public const string CardReason = "Terminated";

    public async Task<Result<TerminationOutcome>> TerminateAsync(Person person, AuditActor actor, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(person);
        var terminated = person.Terminate(now);
        if (terminated.IsFailure)
        {
            return terminated.Error;
        }

        var revokedEntitlements = new List<EntitlementId>();
        var entitlements = await db.Entitlements.Where(e => e.PersonId == person.Id && e.Status == EntitlementStatus.Approved)
            .ToListAsync(ct);
        foreach (var entitlement in entitlements)
        {
            if (entitlement.Revoke(null, Reason, RevocationKind.Termination, now).IsFailure)
            {
                continue;
            }

            var revokeIds = await planner.PlanRevocationAsync(entitlement, CommandPriority.High, actor, now, ct);
            audit.Record(actor, "entitlement.revoked", AuditSubjects.Entitlement, entitlement.Id.Value.ToString(),
                new
                {
                    kind = nameof(RevocationKind.Termination),
                    reason = Reason,
                    revokeFulfillmentIds = revokeIds.Select(r => r.Value).ToList(),
                });
            revokedEntitlements.Add(entitlement.Id);
        }

        var revokedCards = new List<CredentialId>();
        var commandIds = new List<CommandId>();
        var cards = await db.Cards.Where(c => c.PersonId == person.Id && c.State != CardState.Revoked).ToListAsync(ct);
        foreach (var card in cards)
        {
            if (card.Revoke(CardReason, now).IsFailure)
            {
                continue;
            }

            var ids = await cardCommands.QueueSuspensionAsync(card, CardTargetState.Revoked, CardReason, CommandPriority.High, ct);
            audit.Record(actor, "card.revoked", "Card", card.Id.Value.ToString(),
                new
                {
                    personId = person.Id.Value.ToString(),
                    reason = CardReason,
                    commandIds = ids.Select(c => c.Value.ToString()).ToArray(),
                });
            revokedCards.Add(card.Id);
            commandIds.AddRange(ids);
        }

        audit.Record(actor, "person.terminated", "Person", person.Id.Value.ToString(), new
        {
            eligibilityEnd = person.Eligibility.End ?? now,
            revokedEntitlementIds = revokedEntitlements.Select(e => e.Value.ToString()).ToArray(),
            revokedCardIds = revokedCards.Select(c => c.Value.ToString()).ToArray(),
        });
        return new TerminationOutcome(revokedEntitlements, revokedCards, commandIds);
    }

    /// <summary>Wakes the agents of the queued card commands (after the caller saved).</summary>
    public async Task NotifyAsync(TerminationOutcome outcome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.CardCommandIds.Count == 0)
        {
            return;
        }

        var ids = outcome.CardCommandIds.ToList();
        var instances = await db.ConnectorCommands.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.PacsInstanceId)
            .Distinct()
            .ToListAsync(ct);
        await cardCommands.NotifyAsync(instances, ct);
    }
}
