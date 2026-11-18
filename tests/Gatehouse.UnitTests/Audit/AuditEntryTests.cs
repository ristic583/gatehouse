using Gatehouse.Domain.Audit;

namespace Gatehouse.UnitTests.Audit;

public sealed class AuditEntryTests
{
    private static AuditEntry New(string action = "entitlement.requested") =>
        AuditEntry.CreatePending(
            new AuditActor(AuditActorKind.Person, "p"),
            action,
            "Entitlement",
            "e",
            "{}",
            DateTimeOffset.Parse("2026-11-09T10:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Occurred_at_is_truncated_to_microseconds()
    {
        Assert.Equal(1_234_560L, New().OccurredAt.UtcTicks % 10_000_000L);
    }

    [Fact]
    public void Seal_twice_throws()
    {
        var e = New();
        e.Seal(1, AuditHasher.GenesisHash);
        Assert.True(e.HasValidHash());
        Assert.Throws<InvalidOperationException>(() => e.Seal(2, e.Hash));
    }

    [Theory]
    [InlineData("Bad")]
    [InlineData("nodot")]
    [InlineData("a.B")]
    public void Invalid_action_is_rejected(string action)
    {
        Assert.Throws<ArgumentException>(() => New(action));
    }
}
