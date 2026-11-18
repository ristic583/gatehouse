using Gatehouse.Domain.Audit;

namespace Gatehouse.UnitTests.Audit;

public sealed class AuditHasherTests
{
    private const string FirstHash = "1f35db202b6d48c853474fbf65adba8eeda59a2b51313ef900d87257294b5db5";
    private const string SecondHash = "3a4168e2b9d4a8036b862a517fbdac8064679224297577a0fc3beeb227c78540";

    private static AuditEntry First()
    {
        var e = AuditEntry.CreatePending(
            new AuditActor(AuditActorKind.Person, "0190f0e2-0000-7000-8000-000000000001"),
            "entitlement.requested",
            "Entitlement",
            "0190f0e2-0000-7000-8000-000000000002",
            "{\"b\":1,\"a\":\"x\"}",
            DateTimeOffset.Parse("2026-11-09T10:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture));
        e.Seal(1, AuditHasher.GenesisHash);
        return e;
    }

    [Fact]
    public void Known_vector_matches()
    {
        Assert.Equal(FirstHash, First().Hash);
    }

    [Fact]
    public void Second_entry_chains_on_first()
    {
        var second = AuditEntry.CreatePending(
            AuditActor.ForSystem("revocation-monitor"),
            "revocation.unconfirmed_alert",
            "Fulfillment",
            "0190f0e2-0000-7000-8000-000000000003",
            "{}",
            DateTimeOffset.Parse("2026-11-09T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture));
        second.Seal(2, First().Hash);
        Assert.Equal(SecondHash, second.Hash);
    }

    [Theory]
    [InlineData("prev")]
    [InlineData("sequence")]
    [InlineData("occurredAt")]
    [InlineData("actorKind")]
    [InlineData("actorId")]
    [InlineData("action")]
    [InlineData("subjectType")]
    [InlineData("subjectId")]
    [InlineData("payload")]
    public void Changing_any_field_changes_the_hash(string field)
    {
        var t = DateTimeOffset.Parse("2026-11-09T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        string Hash(string f) => AuditHasher.Compute(
            f == "prev" ? "1" : AuditHasher.GenesisHash,
            f == "sequence" ? 2 : 1,
            f == "occurredAt" ? t.AddTicks(10) : t,
            f == "actorKind" ? AuditActorKind.Agent : AuditActorKind.Person,
            f == "actorId" ? "b" : "a",
            f == "action" ? "x.z" : "x.y",
            f == "subjectType" ? "T2" : "T",
            f == "subjectId" ? "2" : "1",
            f == "payload" ? "{\"k\":1}" : "{}");

        Assert.NotEqual(Hash("none"), Hash(field));
    }
}
