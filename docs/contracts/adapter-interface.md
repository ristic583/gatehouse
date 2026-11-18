# PACS adapter contract

The interface every vendor adapter implements, and the conformance suite every adapter must pass. Spec §7.4, §8, §13. Lives in `Gatehouse.Connector.Contracts` (namespace `Gatehouse.Connector.Contracts.Adapters`). Implemented from M4 (LegacyPACS) and M6 (CloudPACS).

## 1. Interface

```csharp
public interface IPacsAdapter
{
    PacsCapabilities Capabilities { get; }

    Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct);
    Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct);      // incl. native start/end
    Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct);
    Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct);
    Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct);                // capability: PinWrite
    Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct);    // unlock, lockdown

    IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync(CancellationToken ct);
    IAsyncEnumerable<PacsRecord> ExportRecordsAsync(ExportScope scope, CancellationToken ct);
}
```

`SetPinAsync` is an addition to the spec's sketch: the spec lists a `SetPin` command, and every command maps to exactly one adapter method.

## 2. Types

```csharp
public sealed record PacsCapabilities(
    string Vendor,                         // "LegacyPacs" | "CloudPacs"
    string AdapterVersion,
    bool NativeValidity,                   // PACS enforces ValidFrom/ValidTo itself
    bool PinWrite,
    bool PhotoWrite,
    bool PushEvents,                       // vendor pushes events (webhook) vs adapter polls
    bool RecordExport,
    IReadOnlyList<DoorCommandKind> DoorCommands,  // subset of MomentaryUnlock, Lockdown
    int MaxBatchSize);

public enum AdapterStatus { Applied, Failed, Unsupported }

public sealed record AdapterError(string Code, string Message, bool Retryable);

public sealed record AdapterResult(AdapterStatus Status, string? PacsRef, AdapterError? Error)
{
    public static AdapterResult Applied(string? pacsRef) => new(AdapterStatus.Applied, pacsRef, null);
    public static AdapterResult Failed(string code, string message, bool retryable) => new(AdapterStatus.Failed, null, new(code, message, retryable));
    public static AdapterResult Unsupported(string capability) => new(AdapterStatus.Unsupported, null, new("adapter.capability_missing", $"{capability} not supported", false));
}

public sealed record Cardholder(string PersonRef, string GivenName, string FamilyName, string? CardNumber, string? PhotoUrl);
public sealed record AccessGrant(string PersonRef, string CardNumber, string VendorAccessLevelRef, DateTimeOffset ValidFrom, DateTimeOffset ValidTo);
public sealed record AccessRevoke(string PersonRef, string CardNumber, string VendorAccessLevelRef);
public enum CardTargetState { Active, Suspended, Revoked }
public sealed record CardStateChange(string PersonRef, string CardNumber, CardTargetState TargetState);
public sealed record PinWrite(string PersonRef, string CardNumber, SensitivePin Pin);   // SensitivePin.ToString() == "****"
public enum DoorCommandKind { MomentaryUnlock, Lockdown }
public enum LockdownAction { Start, Clear }
public sealed record DoorCommand(DoorCommandKind Kind, IReadOnlyList<string> PacsDoorRefs, int? DurationSeconds, LockdownAction? LockdownAction, string Reason);

public sealed record PacsDoorEvent(Guid EventId, DateTimeOffset OccurredAt, DoorEventType Type,
    string PacsDoorRef, string? PacsReaderRef, string? CardNumber, string? PersonRef, ReaderDirection? Direction);

public enum RecordType { Cardholder, Card, AccessAssignment }
public sealed record ExportScope(IReadOnlyList<RecordType> RecordTypes, IReadOnlyList<string>? VendorAccessLevelRefs);
// M4: cursor storage the agent host provides to polling adapters (SQLite table adapter_cursors)
public interface IAdapterCursorStore
{
    ValueTask<string?> GetAsync(string name, CancellationToken ct);
    ValueTask SetAsync(string name, string value, CancellationToken ct);
}

public sealed record PacsRecord(RecordType RecordType, string PacsRef, string? PersonRef, string? CardNumber,
    string? VendorAccessLevelRef, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool IsActive);
```

## 3. Rules for adapters

1. **Never throw for vendor outcomes.** Map them to `AdapterResult`. Exceptions are only for bugs; the agent host catches them and acks `Failed` with `adapter.unhandled_exception`, `retryable: true`.
2. **Retryable** = timeouts, HTTP 429, HTTP 5xx, DB connection errors, LegacyPACS inbox row still unprocessed after the adapter's wait (30 s). **Non-retryable** = unknown access level, unknown cardholder on revoke-with-strict-mode, validation errors from the vendor.
3. **Idempotent at the vendor level where possible** (upsert by `PersonRef`; grant twice = one assignment). The agent's processed-keys store is the primary guard; this is defense in depth.
4. **Capability gaps:** a method whose capability is `false` returns `AdapterResult.Unsupported(...)` without touching the vendor. Exception: `NativeValidity = false` adapters still apply `GrantAccessAsync` (without dates) and the **agent host** schedules a local `RevokeAccess` at `ValidTo` (SQLite table `scheduled_revocations`). For such adapters the agent never applies a grant outside its window (security review GH-CONN-01): `ValidTo <= now` → `Failed("agent.grant_window_ended", retryable: false)`, `ValidFrom > now + 2 min` → `Failed("agent.grant_not_yet_valid", retryable: true)`; the platform holds such grants until `ValidFrom` (`ConnectorCommand.DeferUntil`). The platform UI shows the gap from `PacsCapabilities`.
5. **Events:** `StreamEventsAsync` yields normalized events forever until cancelled. Polling adapters (LegacyPACS) keep a cursor (last `event_log.id`) in agent SQLite (`adapter_cursors`). Push adapters (CloudPACS) receive webhooks on a local listener inside the agent and yield them.
6. **Export:** must include inactive and historical rows with `IsActive = false` (the platform ignores them for findings, spec §9).
7. **Logging:** never log `CardNumber` unmasked, never log `SensitivePin`. Use `CardNumberMask.Mask(string)` → `****1234` (agent-side copy in `Gatehouse.Connector.Contracts`, since the agent cannot reference Domain).

Vendor specifics (from spec §8): LegacyPACS = `NativeValidity: true, PinWrite: false, PhotoWrite: false, PushEvents: false, RecordExport: true`; CloudPACS = `NativeValidity: false, PinWrite: true, PhotoWrite: true, PushEvents: true, RecordExport: true`.

## 4. Conformance suite

Location: `tests/Gatehouse.ContractTests/Conformance/PacsAdapterConformanceTests.cs` - an `abstract class PacsAdapterConformanceTests<TFixture> where TFixture : class, IPacsAdapterFixture`. Each vendor adds a sealed subclass (`LegacyPacsConformanceTests`, `CloudPacsConformanceTests`) whose fixture starts the vendor simulator (Testcontainers / in-process) and builds the adapter.

```csharp
public interface IPacsAdapterFixture : IAsyncLifetime
{
    IPacsAdapter Adapter { get; }
    string KnownAccessLevelRef { get; }        // exists in the simulator
    string KnownPacsDoorRef { get; }
    Task InjectTransientFailureAsync(int count);   // next N vendor calls fail with 5xx / DB timeout
    Task EmitDoorEventAsync(string pacsDoorRef, DoorEventType type, string? cardNumber);
}
```

Test cases (method names are fixed; every adapter must pass all of them):

| # | Test method | Asserts |
|---|---|---|
| C01 | `Capabilities_are_declared` | `Vendor` and `AdapterVersion` non-empty, `MaxBatchSize >= 1` |
| C02 | `UpsertCardholder_twice_creates_one_cardholder` | export `Cardholder` contains the person exactly once |
| C03 | `Grant_then_export_shows_active_assignment` | `AccessAssignment` with ref, card, level, `IsActive = true` |
| C04 | `Revoke_then_export_shows_no_active_assignment` | no active assignment for person + level |
| C05 | `Replayed_grant_does_not_duplicate_assignment` | grant twice → exactly one active assignment |
| C06 | `Grant_honours_validity_or_declares_gap` | `NativeValidity` ⇒ exported `ValidFrom/ValidTo` equal the grant (±1 s); else capability is `false` |
| C07 | `Suspend_then_activate_card_round_trips` | `Card` record inactive after suspend, active after activate |
| C08 | `SetPin_applies_or_returns_unsupported` | `PinWrite` ⇒ `Applied`; else `Unsupported` with `adapter.capability_missing` |
| C09 | `Door_command_applies_or_returns_unsupported` | declared kinds ⇒ `Applied`; others ⇒ `Unsupported` |
| C10 | `Stream_events_yields_normalized_event` | `EmitDoorEventAsync` → a `PacsDoorEvent` with same door ref and type within 10 s |
| C11 | `Transient_vendor_error_is_retryable_failure` | after `InjectTransientFailureAsync(1)` a grant returns `Failed` with `Retryable = true` |
| C12 | `Unknown_access_level_is_non_retryable_failure` | grant with `AL-DOES-NOT-EXIST` → `Failed`, `Retryable = false` |
| C13 | `Export_marks_revoked_rows_inactive_not_missing` | after revoke, export either omits the assignment or returns it with `IsActive = false`; never `IsActive = true` |
| C14 | `Adapter_never_throws_on_vendor_failure` | with transient failure injected, no method throws |

Run: `dotnet test tests/Gatehouse.ContractTests`. Adding a third vendor = add a fixture + sealed subclass and make the suite green.

## 5. CloudPACS adapter (M6)

Project `src/Gatehouse.Adapters.CloudPacs` (namespace `Gatehouse.Adapters.CloudPacs`): `CloudPacsAdapter : IPacsAdapter, IAsyncDisposable`, `CloudPacsAdapterOptions(Uri BaseUrl, string ApiKey, string WebhookListenUrl, string? WebhookPublicUrl = null, TimeSpan? RequestTimeout = null)`.

- **Capabilities:** `new("CloudPacs", "1.0.0", NativeValidity: false, PinWrite: true, PhotoWrite: true, PushEvents: true, RecordExport: true, [MomentaryUnlock], MaxBatchSize: 100)`. Lockdown is a capability gap (`DoorCommandAsync` with `Lockdown` → `Unsupported("lockdown")`).
- **Vendor API:** the simulator's REST API (`simulators/CloudPacs.Simulator`, plan M6-01/02), header `X-Api-Key`. Access level refs are CloudPACS access group ids (`AG-{areaCode}`); `PersonRef` is the cardholder `externalRef`.
- **Error mapping:** timeout → `cloudpacs.timeout`, connection failure → `cloudpacs.unreachable`, 429 → `cloudpacs.rate_limited`, 5xx → `cloudpacs.server_error` (all retryable); 404 `unknown_access_group` → `cloudpacs.unknown_access_level`, 404 `unknown_cardholder` → `cloudpacs.unknown_cardholder`, 404 `unknown_door` → `cloudpacs.unknown_door`, other 4xx → `cloudpacs.rejected` (all non-retryable).
- **Validity:** `GrantAccessAsync` applies the membership without dates (no native validity); the agent host schedules the end (§7).
- **Events (push):** `StreamEventsAsync` starts a local HTTP listener on `WebhookListenUrl` (path `/webhooks/cloudpacs`), registers `WebhookPublicUrl ?? <bound address>/webhooks/cloudpacs` with a per-process random secret, verifies `X-CloudPacs-Signature: sha256=<hex HMAC-SHA256(secret, body)>` (mismatch → 401, event dropped) and yields mapped events. Vendor types map: `access.granted` → `AccessGranted`, `access.denied` → `AccessDenied`, `door.opened` → `DoorOpened`, `door.closed` → `DoorClosed`, `door.held_open` → `DoorHeldOpen`, `door.forced` → `DoorForced`, `door.unlocked` → `DoorUnlocked`, `door.relocked` → `DoorRelocked`; unknown types are skipped (Debug log). `EventId` = vendor event id (Guid).
- **Export:** follows `nextCursor` paging of cardholders, cards and assignments; removed assignments come back with `IsActive = false`; `ValidFrom`/`ValidTo` are `null`.

## 6. Capability gaps in the platform (M6)

`Gatehouse.Application.Connectors.PacsCapabilityGaps` turns a stored `PacsInstance.CapabilitiesJson` into gap codes, in this fixed order: `NativeValidity`, `PinWrite`, `PhotoWrite`, `RecordExport`, `MomentaryUnlock`, `Lockdown` (a code is present when the capability is `false` / the door command kind is missing). `PushEvents = false` is not a gap (polling works). `null` JSON → `null` list (unknown). The UI never hard-codes vendor names: it renders labels for these codes (`src/web/features/connectors/capability-gaps.ts`).

Where gaps show:
- `ConnectorHealthDto.CapabilityGaps` (live health strip, chips);
- `FulfillmentDetailDto.CapabilityGaps` (connector-managed fulfillments; a `NativeValidity` gap on a grant reads "end date enforced by the agent");
- `LiveDoorDto.CanUnlock` / `CanLockdown` (door actions); door commands skip doors whose instance lacks the kind (`DoorCommandResultDto.SkippedDoorCount`) and fail with `door_command.unsupported_by_pacs` when none is left.

## 7. Agent-scheduled revocations (M6)

For adapters with `NativeValidity = false` the agent host keeps SQLite `scheduled_revocations` (`id`, `person_ref`, `card_number`, `vendor_access_level_ref`, `revoke_at`, `done`; one pending row per `(person_ref, vendor_access_level_ref)`):
- `GrantAccess` acked `Applied` → upsert the pending row with `revoke_at = ValidTo`;
- `RevokeAccess` acked `Applied` → mark the pending row done;
- `ScheduledRevocationLoop` (every 30 s) calls `RevokeAccessAsync` for due rows; `Applied` → done, retryable → try again next tick, non-retryable `Failed` → logged at Error, recorded in SQLite `refused_removals` (reported in every heartbeat as `refusedRemovals`, `connector-protocol.md` §2.5, until the removal is applied or cancelled) and retried after 15 min (never abandoned: GH-CONN-03). Card numbers are logged masked only.
The platform stays the source of truth: its own expiry/revocation commands still arrive and are idempotent with the local one.

**Grant fence (M11-13, GH-CONN-02).** For every adapter the agent host also keeps SQLite `fences` (`person_ref`, `vendor_access_level_ref`, `sequence`, `created_at`; one row per pair, highest sequence wins, rows older than 7 days pruned). A `RevokeAccess` with `priority = High` records a fence with its command `sequence` before it executes; a later-arriving `GrantAccess` for the same pair with a lower `sequence` is acked `Failed("agent.grant_fenced", retryable: false)` without calling the adapter. A grant with a higher sequence is not fenced; a grant that already started completes and the revoke follows in partition order. The platform keeps the grant's fulfillment `Superseded` on that ack (no failed-fulfillment task).
