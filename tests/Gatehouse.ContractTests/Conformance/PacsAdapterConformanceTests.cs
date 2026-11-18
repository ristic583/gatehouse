using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.ContractTests.Conformance;

public abstract class PacsAdapterConformanceTests<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : class, IPacsAdapterFixture
{
    private static readonly DateTimeOffset From = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly ExportScope All = new([RecordType.Cardholder, RecordType.Card, RecordType.AccessAssignment], null);

    private IPacsAdapter Adapter => fixture.Adapter;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewId() => Guid.NewGuid().ToString("N")[..10];

    private AccessGrant NewGrant(string person, string card) => new(person, card, fixture.KnownAccessLevelRef, From, From.AddDays(14));

    private async Task<List<PacsRecord>> ExportAsync()
    {
        var list = new List<PacsRecord>();
        await foreach (var r in Adapter.ExportRecordsAsync(All, Ct))
        {
            list.Add(r);
        }

        return list;
    }

    private async Task<List<PacsRecord>> AssignmentsAsync(string person) =>
        (await ExportAsync()).Where(r => r.RecordType == RecordType.AccessAssignment && r.PersonRef == person && r.VendorAccessLevelRef == fixture.KnownAccessLevelRef).ToList();

    private async Task SeedAsync(string person, string card) =>
        Assert.Equal(AdapterStatus.Applied, (await Adapter.UpsertCardholderAsync(new Cardholder(person, "Test", "Person", card, null), Ct)).Status);

    [Fact]
    public void C01_Capabilities_are_declared()
    {
        var c = Adapter.Capabilities;
        Assert.False(string.IsNullOrWhiteSpace(c.Vendor));
        Assert.False(string.IsNullOrWhiteSpace(c.AdapterVersion));
        Assert.True(c.MaxBatchSize >= 1);
    }

    [Fact]
    public async Task C02_UpsertCardholder_twice_creates_one_cardholder()
    {
        var person = "p" + NewId();
        await SeedAsync(person, "9" + NewId());
        await SeedAsync(person, "9" + NewId());
        Assert.Single(await ExportAsync(), r => r.RecordType == RecordType.Cardholder && r.PersonRef == person);
    }

    [Fact]
    public async Task C03_Grant_then_export_shows_active_assignment()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        var result = await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        Assert.Equal(AdapterStatus.Applied, result.Status);
        var a = Assert.Single(await AssignmentsAsync(person));
        Assert.True(a.IsActive);
        Assert.Equal(card, a.CardNumber);
        Assert.False(string.IsNullOrEmpty(a.PacsRef));
    }

    [Fact]
    public async Task C04_Revoke_then_export_shows_no_active_assignment()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        var result = await Adapter.RevokeAccessAsync(new AccessRevoke(person, card, fixture.KnownAccessLevelRef), Ct);
        Assert.Equal(AdapterStatus.Applied, result.Status);
        Assert.DoesNotContain(await AssignmentsAsync(person), r => r.IsActive);
    }

    [Fact]
    public async Task C05_Replayed_grant_does_not_duplicate_assignment()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        Assert.Single(await AssignmentsAsync(person), r => r.IsActive);
    }

    [Fact]
    public async Task C06_Grant_honours_validity_or_declares_gap()
    {
        if (!Adapter.Capabilities.NativeValidity)
        {
            Assert.False(Adapter.Capabilities.NativeValidity);
            return;
        }

        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        var grant = NewGrant(person, card);
        await Adapter.GrantAccessAsync(grant, Ct);
        var a = Assert.Single(await AssignmentsAsync(person));
        Assert.True(Math.Abs((a.ValidFrom!.Value - grant.ValidFrom).TotalSeconds) <= 1);
        Assert.True(Math.Abs((a.ValidTo!.Value - grant.ValidTo).TotalSeconds) <= 1);
    }

    [Fact]
    public async Task C07_Suspend_then_activate_card_round_trips()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        Assert.Equal(AdapterStatus.Applied, (await Adapter.SetCardStateAsync(new CardStateChange(person, card, CardTargetState.Suspended), Ct)).Status);
        Assert.False(Assert.Single(await ExportAsync(), r => r.RecordType == RecordType.Card && r.CardNumber == card).IsActive);
        Assert.Equal(AdapterStatus.Applied, (await Adapter.SetCardStateAsync(new CardStateChange(person, card, CardTargetState.Active), Ct)).Status);
        Assert.True(Assert.Single(await ExportAsync(), r => r.RecordType == RecordType.Card && r.CardNumber == card).IsActive);
    }

    [Fact]
    public async Task C08_SetPin_applies_or_returns_unsupported()
    {
        var result = await Adapter.SetPinAsync(new PinWrite("p" + NewId(), "9" + NewId(), new SensitivePin("1234")), Ct);
        if (Adapter.Capabilities.PinWrite)
        {
            Assert.Equal(AdapterStatus.Applied, result.Status);
        }
        else
        {
            Assert.Equal(AdapterStatus.Unsupported, result.Status);
            Assert.Equal("adapter.capability_missing", result.Error!.Code);
        }
    }

    [Fact]
    public async Task C09_Door_command_applies_or_returns_unsupported()
    {
        foreach (var kind in Enum.GetValues<DoorCommandKind>())
        {
            var command = new DoorCommand(kind, [fixture.KnownPacsDoorRef], kind == DoorCommandKind.MomentaryUnlock ? 5 : null,
                kind == DoorCommandKind.Lockdown ? LockdownAction.Start : null, "conformance");
            var result = await Adapter.DoorCommandAsync(command, Ct);
            Assert.Equal(Adapter.Capabilities.DoorCommands.Contains(kind) ? AdapterStatus.Applied : AdapterStatus.Unsupported, result.Status);
        }
    }

    [Fact]
    public async Task C10_Stream_events_yields_normalized_event()
    {
        var card = "9" + NewId();
        await fixture.EmitDoorEventAsync(fixture.KnownPacsDoorRef, DoorEventType.DoorForced, card);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        PacsDoorEvent? found = null;
        await foreach (var e in Adapter.StreamEventsAsync(cts.Token))
        {
            if (e.CardNumber == card)
            {
                found = e;
                break;
            }
        }

        Assert.NotNull(found);
        Assert.Equal(fixture.KnownPacsDoorRef, found.PacsDoorRef);
        Assert.Equal(DoorEventType.DoorForced, found.Type);
    }

    [Fact]
    public async Task C11_Transient_vendor_error_is_retryable_failure()
    {
        await fixture.InjectTransientFailureAsync(1);
        var result = await Adapter.GrantAccessAsync(NewGrant("p" + NewId(), "9" + NewId()), Ct);
        Assert.Equal(AdapterStatus.Failed, result.Status);
        Assert.True(result.Error!.Retryable);
    }

    [Fact]
    public async Task C12_Unknown_access_level_is_non_retryable_failure()
    {
        var grant = NewGrant("p" + NewId(), "9" + NewId()) with { VendorAccessLevelRef = "AL-DOES-NOT-EXIST" };
        var result = await Adapter.GrantAccessAsync(grant, Ct);
        Assert.Equal(AdapterStatus.Failed, result.Status);
        Assert.False(result.Error!.Retryable);
    }

    [Fact]
    public async Task C13_Export_marks_revoked_rows_inactive_not_missing()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await SeedAsync(person, card);
        await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        await Adapter.RevokeAccessAsync(new AccessRevoke(person, card, fixture.KnownAccessLevelRef), Ct);
        Assert.All(await AssignmentsAsync(person), r => Assert.False(r.IsActive));
    }

    [Fact]
    public async Task C14_Adapter_never_throws_on_vendor_failure()
    {
        var (person, card) = ("p" + NewId(), "9" + NewId());
        await fixture.InjectTransientFailureAsync(1);
        var door = new DoorCommand(DoorCommandKind.MomentaryUnlock, [fixture.KnownPacsDoorRef], 5, null, "conformance");
        await Adapter.UpsertCardholderAsync(new Cardholder(person, "T", "P", card, null), Ct);
        await Adapter.GrantAccessAsync(NewGrant(person, card), Ct);
        await Adapter.RevokeAccessAsync(new AccessRevoke(person, card, fixture.KnownAccessLevelRef), Ct);
        await Adapter.SetCardStateAsync(new CardStateChange(person, card, CardTargetState.Suspended), Ct);
        await Adapter.SetPinAsync(new PinWrite(person, card, new SensitivePin("1234")), Ct);
        await Adapter.DoorCommandAsync(door, Ct);
    }
}
