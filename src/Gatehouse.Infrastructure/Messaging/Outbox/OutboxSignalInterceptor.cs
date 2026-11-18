using System.Data.Common;
using Gatehouse.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

/// <summary>
/// Signals <see cref="IOutboxSignal"/> after a commit that wrote outbox rows (conventions §4.8: side effects after commit), so the
/// relay visits this tenant on its next tick. Scoped: one instance per <c>TenantDbContext</c>, registered after
/// <see cref="DomainEventOutboxInterceptor"/> so it sees the rows that interceptor adds. Signalling twice is harmless (one extra visit).
/// </summary>
public sealed class OutboxSignalInterceptor(ITenantContext tenant, IOutboxSignal signal) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private bool _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Track(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Track(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        NotifyIfCommitted(eventData);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfCommitted(eventData);
        return base.SavedChanges(eventData, result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Notify();

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Notify();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _pending = false;

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Track(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context
            && context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added))
        {
            _pending = true;
        }
    }

    // Without an explicit transaction SaveChanges has committed when it returns; inside one, the commit signals.
    private void NotifyIfCommitted(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            Notify();
        }
    }

    private void Notify()
    {
        if (_pending && tenant.IsResolved)
        {
            _pending = false;
            signal.Notify(tenant.TenantId);
        }
    }
}
