using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.UnitTests.Connectors;

public sealed class ConnectorCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static ConnectorCommand New() =>
        ConnectorCommand.Create(PacsInstanceId.New(), "GrantAccess", "person:1", 1, CommandPriority.Normal, "GrantAccess:f:1", "{}", FulfillmentId.New(), Now);

    private static ConnectorCommand Delivered(int times)
    {
        var c = New();
        for (var i = 0; i < times; i++)
        {
            if (i > 0)
            {
                c.ExpireLease(Now.AddHours(i));
            }

            c.Deliver(Now.AddHours(i));
        }

        return c;
    }

    [Fact]
    public void Deliver_sets_lease_and_increments_attempts()
    {
        var c = New();
        Assert.True(c.Deliver(Now).IsSuccess);
        Assert.Equal(ConnectorCommandStatus.Delivered, c.Status);
        Assert.Equal(1, c.Attempts);
        Assert.Equal(Now.AddSeconds(120), c.LeaseUntil);
        Assert.Equal(Now, c.DeliveredAt);
    }

    [Fact]
    public void Expired_lease_requeues_with_same_key()
    {
        var c = Delivered(1);
        var key = c.IdempotencyKey;
        Assert.Equal(CommandOutcome.Requeued, c.ExpireLease(Now.AddSeconds(121)));
        Assert.Equal(ConnectorCommandStatus.Queued, c.Status);
        Assert.Equal(key, c.IdempotencyKey);
    }

    [Fact]
    public void Fifth_lease_expiry_fails_command()
    {
        var c = Delivered(5);
        Assert.Equal(CommandOutcome.Failed, c.ExpireLease(Now.AddDays(1)));
        Assert.Equal(ConnectorCommandStatus.Failed, c.Status);
        Assert.Equal("connector.lease_expired", c.LastError);
    }

    [Fact]
    public void Applied_ack_marks_acked()
    {
        var c = Delivered(1);
        Assert.Equal(CommandOutcome.Acked, c.ApplyAck(CommandAckStatus.Applied, "ref", null, null, false, Now));
        Assert.Equal(ConnectorCommandStatus.Acked, c.Status);
        Assert.Equal("ref", c.PacsRef);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void Retryable_failure_schedules_backoff(int attempts, int seconds)
    {
        var c = Delivered(attempts);
        Assert.Equal(CommandOutcome.RetryScheduled, c.ApplyAck(CommandAckStatus.Failed, null, "x", "m", true, Now));
        Assert.Equal(ConnectorCommandStatus.Queued, c.Status);
        Assert.Equal(Now.AddSeconds(seconds), c.NextAttemptAt);
    }

    [Fact]
    public void Retryable_failure_on_fifth_attempt_fails()
    {
        var c = Delivered(5);
        Assert.Equal(CommandOutcome.Failed, c.ApplyAck(CommandAckStatus.Failed, null, "x", "m", true, Now));
        Assert.Equal(ConnectorCommandStatus.Failed, c.Status);
    }

    [Theory]
    [InlineData(CommandAckStatus.Failed, false)]
    [InlineData(CommandAckStatus.Unsupported, false)]
    [InlineData(CommandAckStatus.Unsupported, true)]
    public void Non_retryable_and_unsupported_fail_immediately(CommandAckStatus status, bool retryable)
    {
        var c = Delivered(1);
        Assert.Equal(CommandOutcome.Failed, c.ApplyAck(status, null, "x", "m", retryable, Now));
        Assert.Equal(ConnectorCommandStatus.Failed, c.Status);
    }

    [Fact]
    public void Late_ack_after_requeue_is_accepted()
    {
        var c = Delivered(1);
        c.ExpireLease(Now.AddSeconds(200));
        Assert.Equal(ConnectorCommandStatus.Queued, c.Status);
        Assert.Equal(CommandOutcome.Acked, c.ApplyAck(CommandAckStatus.Applied, "r", null, null, false, Now.AddSeconds(201)));
    }

    [Fact]
    public void Duplicate_final_ack_same_status_is_duplicate()
    {
        var c = Delivered(1);
        c.ApplyAck(CommandAckStatus.Applied, "r", null, null, false, Now);
        Assert.Equal(CommandOutcome.Duplicate, c.ApplyAck(CommandAckStatus.Applied, "r", null, null, false, Now));
    }

    [Fact]
    public void Final_ack_with_other_status_conflicts()
    {
        var c = Delivered(1);
        c.ApplyAck(CommandAckStatus.Applied, "r", null, null, false, Now);
        Assert.Equal(CommandOutcome.Conflict, c.ApplyAck(CommandAckStatus.Failed, null, "x", "m", false, Now));
    }

    [Fact]
    public void Supersede_only_from_queued()
    {
        Assert.True(New().Supersede(Now));
        Assert.False(Delivered(1).Supersede(Now));
    }
}
