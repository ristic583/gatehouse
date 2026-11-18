using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.SetPin;

/// <summary>
/// <c>PUT /api/v1/me/pin</c> (<c>LinkedPerson</c>): stores the PIN envelope-encrypted and queues <c>SetPin</c> (vault reference only) for
/// my
/// Active card on every PIN-write connector instance. Audits <c>pin.set</c> without the value.
/// </summary>
public sealed class SetPinHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IPinVault vault,
    CardCommandService cardCommands,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(SetPinRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pin = Pin.Create(request.Pin);
        if (pin.IsFailure)
        {
            return pin.Error;
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == me, ct);
        if (person.Status == PersonStatus.Terminated)
        {
            return CardAccess.Terminated();
        }

        var now = time.GetUtcNow();
        var (ciphertext, keyId) = await vault.EncryptAsync(pin.Value, ct);
        var existing = await db.PinCredentials.FirstOrDefaultAsync(p => p.Id == me, ct);
        if (existing is null)
        {
            db.PinCredentials.Add(PinCredential.Set(me, ciphertext, keyId, me, now));
        }
        else
        {
            existing.Replace(ciphertext, keyId, me, now);
        }

        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.PersonId == me && c.State == CardState.Active, ct);
        IReadOnlyList<CommandId> queued = [];
        if (card is not null)
        {
            queued = await cardCommands.QueueSetPinAsync(me, card.CardNumber, vault.ProtectForTransport(ciphertext, keyId), ct);
        }

        audit.Record(
            AuditActor.ForPerson(me), "pin.set", "Person", me.Value.ToString(), new { commandIds = queued.Select(c => c.Value).ToList() });
        await db.SaveChangesAsync(ct);
        await cardCommands.NotifyAsync(await CardErrors.InstancesOfAsync(db, queued, ct), ct);
        return Result.Success();
    }
}
