# Domain model contract

Source of truth for entity names, fields, invariants, state machines and error codes. Spec: `docs/SPEC.md` §4-§6, §9-§10. Decisions: ADR-001 (tenancy), ADR-005 (outbox), ADR-006 (door events).

## 0. Conventions

- **Namespaces:** entities live in `Gatehouse.Domain.{Module}` (e.g. `Gatehouse.Domain.Sites.Area`). Shared kernel lives in `Gatehouse.Domain.Common`.
- **Ids:** strongly typed `readonly record struct {Name}Id(Guid Value)` with `static {Name}Id New() => new(Guid.CreateVersion7())`. Exceptions: `TenantId` (string slug, see below), `AuditEntry.Sequence` (`long`), `DoorEvent.Id` (`Guid` from the agent).
- **Time:** all instants are `DateTimeOffset` in UTC, stored as `timestamptz`. Use `TimeProvider` (inject it; tests use `FakeTimeProvider`). Domain methods never read the clock; they take `DateTimeOffset now`.
- **Time zones:** IANA ids (`Europe/Copenhagen`) stored as `string`, used only for display and for "end of day" calculations.
- **Persistence names:** tables and columns are `snake_case` (EFCore.NamingConventions `UseSnakeCaseNamingConvention()`); table names are plural (`areas`, `approval_steps`).
- **Enums:** stored as strings (`HasConversion<string>()`), serialized as strings in JSON.
- **Concurrency:** aggregates marked *(versioned)* have `public uint Version { get; private set; }` mapped with `.IsRowVersion()` (Npgsql maps it to `xmin`).
- **Behaviour:** state changes only through methods on the aggregate that return `Result`/`Result<T>`; setters are `private`. Methods raise domain events via `Raise(IDomainEvent)` on `AggregateRoot`.

### Shared kernel (`Gatehouse.Domain.Common`)

| Type | Shape |
|---|---|
| `TenantId` | `readonly record struct TenantId(string Value)`; `static Result<TenantId> Create(string)` validates `^[a-z][a-z0-9-]{2,31}$`; `ToString()` returns `Value` |
| `Error` | `sealed record Error(string Code, string Message, ErrorKind Kind)`; static factories `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `Failure` |
| `ErrorKind` | `Validation, NotFound, Conflict, Forbidden, Unauthorized, Failure` |
| `Result` / `Result<T>` | `IsSuccess`, `IsFailure`, `Error` (non-null iff failure), `Value` (throws on failure); `Result.Success()`, `Result.Failure(Error)`, implicit conversions from `T` and from `Error` |
| `Entity<TId>` | `TId Id` |
| `AggregateRoot<TId>` | `Entity<TId>` + `IReadOnlyList<IDomainEvent> DomainEvents`, `Raise(IDomainEvent)`, `ClearDomainEvents()` |
| `IDomainEvent` | `Guid EventId { get; }`, `DateTimeOffset OccurredAt { get; }` |
| `Point` | `readonly record struct Point(double X, double Y)` |
| `Polygon` | `sealed record Polygon(IReadOnlyList<Point> Points)`; `static Result<Polygon> Create(IEnumerable<Point>, double width, double height)` |
| `DateRange` | `readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset? End)`; `bool Contains(DateTimeOffset)` is `Start <= t && (End is null || t < End)` (end exclusive) |

---

## 1. Catalog database (`CatalogDbContext`)

### Tenant
| Field | Type | Rules |
|---|---|---|
| `Id` | `TenantId` | PK, immutable |
| `DisplayName` | `string` | 1-100 chars |
| `Status` | `TenantStatus` | `Provisioning` (M10), `ProvisioningFailed` (M10), `Active`, `Suspended`, `Deprovisioned`; only `Active` resolves (`tenant.inactive` otherwise) |
| `DatabaseName` | `string` | `gh_tenant_{slug with - → _}` |
| `EncryptedConnectionString` | `string` | output of `ISecretProtector.Protect`; never logged, never returned by any API |
| `IdpKind` | `IdpKind` | `Oidc`, `Saml`, `Local` |
| `KeycloakOrganizationAlias` | `string` | equals `Id.Value` |
| `CreatedAt` | `DateTimeOffset` | |
| `EmailDomain` | `string?` | (M10) lowercase DNS name, unique across tenants when set; owned by the Keycloak organization (verified); set by the platform operator only. Demo tenants backfilled `nordlys.test`, `bricks.test` |
| `SuspendedAt`, `SuspendedReason` | `DateTimeOffset?`, `string?` | (M10) reason 1-500 chars |

Transitions (M10, `tenant.invalid_status_transition` otherwise): `Provisioning → Active` (`MarkProvisioned`), `Provisioning → ProvisioningFailed` (`MarkProvisioningFailed`), `ProvisioningFailed → Provisioning` (`RetryProvisioning`), `Active → Suspended` (`Suspend(reason, now)`), `Suspended → Active` (`Reactivate`). `Suspend`/`Activate` become `Result`-returning.

### TenantProvisioning (M10, `Gatehouse.Domain.Platform`)
| Field | Type | Rules |
|---|---|---|
| `TenantId` | `TenantId` | PK, FK → Tenant |
| `Step` | `ProvisioningStep` | `Database`, `Migrate`, `Baseline`, `FirstAdmin`, `Organization`, `AdminInvite`, `Completed` (in this order) |
| `State` | `ProvisioningState` | `Running`, `Failed`, `Completed` |
| `Attempts` | `int` | attempts of the current step |
| `LastError` | `string?` | ≤ 1000 chars, never secrets or connection strings |
| `AdminEmail`, `AdminGivenName`, `AdminFamilyName` | `string` | first tenant admin (same rules as Person) |
| `TimeZoneId` | `string` | IANA; first admin's eligibility zone and default for new sites |
| `StartedAt`, `UpdatedAt`, `CompletedAt` | `DateTimeOffset`, `DateTimeOffset`, `DateTimeOffset?` | |
| `AdminInvitedAt`, `AdminActivatedAt` | `DateTimeOffset?` | activated = first sign-in seen by the statistics job |

Methods: `Start(...)`, `Advance(now)` (next step; `Completed` ⇒ `State = Completed`), `Fail(error, now)`, `Retry(now)` (`Failed` only, else `tenant.provisioning_not_failed`). Table `tenant_provisioning`.

### PlatformOperator (M10, `Gatehouse.Domain.Platform`)
`Id` (`PlatformOperatorId`), `Email` (lowercase, unique), `DisplayName` (1-100), `Subject` (`string?`, platform realm `sub`, unique when set), `Status` (`Active`, `Disabled`), `CreatedAt`. Table `platform_operators`. Created by CLI `bootstrap` from `Platform:BootstrapOperatorEmail` (operator management UI is out of M10).

### PlatformAuditEntry (M10)
Table `platform_audit_entries`: `Id`, `Sequence` (bigint, unique), `OccurredAt`, `ActorKind` (`Operator`, `System`), `ActorId` (`string`), `Action`, `TenantId?`, `Data` (jsonb, no secrets), `PreviousHash`, `Hash` (same canonical JSON + SHA-256 chaining as `AuditHasher`). Append-only via trigger (as `audit_entries`). Actions: `platform.tenant_created`, `platform.tenant_provisioned`, `platform.tenant_provisioning_failed`, `platform.tenant_provisioning_retried`, `platform.tenant_suspended`, `platform.tenant_reactivated`, `platform.admin_invite_sent`.

### TenantStatistics (M10)
Table `tenant_statistics` (PK `TenantId`), written only by `TenantStatisticsJob` (Workers, every 5 min, tenancy rule 3), counts only: `People`, `Employees`, `TenantAdmins`, `Sites`, `Floors`, `Areas`, `Doors`, `PacsInstances`, `ConnectorsOnline`, `ConnectorsOffline`, `ConnectorsNeverSeen`, `ActiveEntitlements`, `PendingRequests`, `OpenAlarms`, `UnconfirmedFulfillments` (> 1 h, as the dashboard), `LinkedPeople` (persons with `IdentitySubject`), `OnboardingCompletedAt?`, `ComputedAt`.

**Platform alerts** (computed on read, not stored; `PlatformAlertDto(Kind, Severity, TenantId, Message, Since)`): `ProvisioningFailed` (error), `ConnectorOffline` (warning; statistics `ConnectorsOffline > 0`), `CertificateExpiring` (warning; active registration `CertificateNotAfter < now + 14 d`), `EnrollmentTokenExpired` (info; `PendingEnrollment` past `EnrollmentTokenExpiresAt`), `StatisticsStale` (warning; `ComputedAt` older than 15 min for an `Active` tenant), `AdminNotActivated` (info; invited > 72 h ago, not activated).

### ConnectorRegistration (M10 note)
Also created by `POST /api/v1/admin/pacs-instances/{id}/connector` (`ConnectorRegistrationService.CreateForInstanceAsync(tenantId, pacsInstanceId, ct)`), revoked by `.../connector/revoke` (`ConnectorRegistration.Revoke(now)`; the instance returns to `Manual`). The gateway refuses every request of a registration whose tenant is not `Active` (`connector.tenant_inactive`).

### ConnectorRegistration (M4, `Gatehouse.Domain.Connectors`)
| Field | Type | Rules |
|---|---|---|
| `Id` | `ConnectorId` | PK |
| `TenantId` | `TenantId` | FK → Tenant |
| `PacsInstanceId` | `PacsInstanceId` | the tenant-DB PacsInstance it serves; unique per tenant |
| `DisplayName` | `string` | |
| `Status` | `ConnectorStatus` | `PendingEnrollment → Active → Revoked` (Revoked is terminal) |
| `EnrollmentTokenHash` | `string?` | SHA-256 hex of the one-time token; cleared on enrollment |
| `EnrollmentTokenExpiresAt` | `DateTimeOffset?` | 24 h after creation |
| `CertificateThumbprint` | `string?` | SHA-256 hex of the issued client cert |
| `CertificateNotAfter` | `DateTimeOffset?` | |
| `CreatedAt`, `LastSeenAt` | `DateTimeOffset`, `DateTimeOffset?` | |

Table `connector_registrations`. Created by CLI `connector create --tenant <id> --pacs <name>` (prints the token once: `ghe_` + 32 random bytes base64url; sets the instance's `FulfillmentMode = Connector`). Creating a second non-revoked registration for the same instance → `connector.already_exists`.

### ConnectorAuthority (M4, `Gatehouse.Domain.Connectors`)
Table `connector_authorities`, at most one row: `Id` (`Guid`), `CertificatePem` (`string`, self-signed RSA 3072 CA, 10 years), `EncryptedPrivateKeyPem` (`string`, via `ISecretProtector`), `CreatedAt`. Created at bootstrap by `ConnectorCertificateAuthority.EnsureCreatedAsync`; issues client certificates per `connector-protocol.md` §1.

---

## 2. Tenant database (`TenantDbContext`)

### 2.1 Sites module

**Site** (aggregate)
| Field | Type | Rules |
|---|---|---|
| `Id` | `SiteId` | |
| `Name` | `string` | 1-100, unique per tenant |
| `Code` | `string` | `^[A-Z0-9-]{2,20}$`, unique (e.g. `CPH-HQ`) |
| `TimeZoneId` | `string` | valid IANA id (`TimeZoneInfo.FindSystemTimeZoneById` succeeds) |

**Floor** (aggregate)
| Field | Type | Rules |
|---|---|---|
| `Id` | `FloorId` | |
| `SiteId` | `SiteId` | |
| `Name` | `string` | 1-100 |
| `Level` | `int` | unique per site, -5..200 |
| `PlanWidth`, `PlanHeight` | `int` | 100..10000 (SVG user units) |
| `Background` | owned, nullable | (M10) `ObjectKey`, `ContentType` (`image/jpeg`/`image/png`), `WidthPx`, `HeightPx` (≤ 4096), `UpdatedAt`; columns `background_*`; image stretched to the plan, geometry stays in plan units |

**Area** (aggregate, versioned)
| Field | Type | Rules |
|---|---|---|
| `Id` | `AreaId` | |
| `FloorId` | `FloorId` | |
| `Name` | `string` | 1-100, unique per floor |
| `Code` | `string` | `^[A-Z0-9-]{2,30}$`, unique per tenant (e.g. `CPH-SERVER`) |
| `Polygon` | `Polygon` | ≥ 3 points, all inside `[0,PlanWidth]×[0,PlanHeight]` of its floor, not self-intersecting; stored as `jsonb` |
| `SecurityLevelId` | `SecurityLevelId` | |
| `Owners` | `IReadOnlyCollection<AreaOwner>` | **≥ 1** at all times; owned entity table `area_owners (area_id, person_id)` |
| `AccessControlled` | `bool` | default `true`; `false` = no PACS door (public corridor, storage): excluded from mapping completeness (onboarding, `UnmappedAreas`) and from requests (`request.area_not_access_controlled`) |

Invariants: `RemoveOwner` fails with `area.last_owner` if it would leave 0 owners. An owner must be an active Person of type `Employee`.

**Door**
| Field | Type | Rules |
|---|---|---|
| `Id` | `DoorId` | |
| `AreaId` | `AreaId` | the area the door leads **into**; `MoveTo` changes it within the same floor |
| `Name` | `string` | |
| `Position` | `Point` | on the floor plan |
| `PacsDoorRef` | `string?` | vendor door id; unique per PACS instance when set |

**Reader**
| Field | Type | Rules |
|---|---|---|
| `Id` | `ReaderId` | |
| `DoorId` | `DoorId` | |
| `Name` | `string` | e.g. `R-2.04` |
| `Direction` | `ReaderDirection` | `In`, `Out` |
| `Position` | `Point` | |
| `PacsReaderRef` | `string?` | |

**SecurityLevel** (aggregate)
| Field | Type | Rules |
|---|---|---|
| `Id` | `SecurityLevelId` | |
| `Name` | `string` | unique per tenant |
| `Rank` | `int` | 1 = lowest; unique |
| `ApproverChain` | `ApproverChain` | `Manager`, `AreaOwner`, `Both` (`Both` = Manager **then** AreaOwner, in that order) |
| `MaxDurationDays` | `int` | 1..3650 |

Default seed per tenant: Low (1, Manager, 365), Medium (2, AreaOwner, 365), High (3, Both, 182), Restricted (4, Both, 30).

### 2.2 People module

**Person** (aggregate, versioned)
| Field | Type | Rules |
|---|---|---|
| `Id` | `PersonId` | |
| `Source` | `PersonSource` | `Authoritative` (HR/IdP feed), `Local` |
| `Type` | `PersonType` | `Employee`, `External`, `Visitor`, `Guest` |
| `GivenName`, `FamilyName` | `string` | 1-100 |
| `Email` | `string` | lowercase, unique per tenant |
| `IdentitySubject` | `string?` | Keycloak `sub`; set on first sign-in; unique when set |
| `ManagerId` | `PersonId?` | ≠ `Id` |
| `SponsorId` | `PersonId?` | ≠ `Id`; sponsor must be an `Employee` |
| `Status` | `PersonStatus` | `Active`, `Terminated` |
| `Eligibility` | `Eligibility` | owned, 1:1, always present |

**Eligibility** (owned by Person)
| Field | Type | Rules |
|---|---|---|
| `Start` | `DateTimeOffset` | |
| `End` | `DateTimeOffset?` | `null` = open-ended (allowed only for `Authoritative` employees); `End > Start` |
| `TimeZoneId` | `string` | IANA |

Invariants (enforced in `Person.Create` / `Person.Update`, error codes in §4):
- `Type != Employee` ⇒ `Source == Local`.
- `Source == Local` ⇒ `SponsorId != null` **and** `Eligibility.End != null` (spec §3, §16: "cannot be saved").
- Eligibility is **the outer limit of every entitlement**.
- Lifecycle caps (sponsorship ≤ 365 days, visitor ≤ 7 days) are tenant settings enforced from M8.

**RoleAssignment** (only for roles that are not derived from data)
| Field | Type | Rules |
|---|---|---|
| `Id` | `RoleAssignmentId` | |
| `PersonId` | `PersonId` | |
| `Role` | `AssignedRole` | `SecurityStaff`, `TenantAdmin` |
| `SiteId` | `SiteId?` | `null` = tenant-wide; only `SecurityStaff` may have a site scope |

Derived roles (never stored): `Employee`/`External`/`Visitor`/`Guest` from `Person.Type`; `Manager` = has ≥ 1 active person with `ManagerId == me`; `Sponsor` = has ≥ 1 active person with `SponsorId == me`; `AreaOwner` = appears in any `Area.Owners`.

### 2.3 Credentials module (fields fixed now; `Card` aggregate + state machine + `cards` table from M4, card endpoints and other behaviour from M8)

**Card** (aggregate, versioned)
| Field | Type | Rules |
|---|---|---|
| `Id` | `CredentialId` | |
| `PersonId` | `PersonId` | |
| `CardNumber` | `string` | `^[0-9]{6,20}$` (else `card.invalid_number`); logged only masked (`****1234`) |
| `State` | `CardState` | see state machine |
| `StateChangedAt` | `DateTimeOffset` | |

Card state machine:
```
Assigned ──RequestActivation──▶ ActivationPending ──ActivationConfirmed──▶ Active
ActivationPending ──ActivationFailed──▶ Assigned
Active ──Deactivate──▶ Deactivated ──RequestActivation──▶ ActivationPending
{Assigned, ActivationPending, Active, Deactivated} ──Revoke──▶ Revoked   (terminal)
```
Any other transition returns `card.invalid_transition`.

**PinCredential**: `PersonId`, `Ciphertext` (`byte[]`, envelope-encrypted), `KeyId` (`string`), `SetAt`. The `Pin` value object's `ToString()` returns `"****"`; there is no property that returns the plain value except `Pin.Reveal()` used only by the PIN reveal handler and the `SetPin` command builder.

**Photo**: `PersonId`, `ObjectKey`, `ContentType` (`image/jpeg` | `image/png`), `SizeBytes` (≤ 2 MiB), `UploadedAt`.

**Invariant (spec §4):** card state never creates, broadens or removes entitlements, and entitlement changes never change card state. No Access-module method takes a Card; no Credentials-module method takes an Entitlement.

### 2.4 Access module

**Entitlement** (aggregate, versioned)
| Field | Type | Rules |
|---|---|---|
| `Id` | `EntitlementId` | |
| `PersonId` | `PersonId` | the subject |
| `AreaId` | `AreaId` | |
| `RequestedById` | `PersonId` | may equal `PersonId` |
| `RequestedStart`, `RequestedEnd` | `DateTimeOffset` | `RequestedStart < RequestedEnd` |
| `EffectiveEnd` | `DateTimeOffset` | `min(RequestedEnd, Eligibility.End)`; recomputed when eligibility changes |
| `CappedToEligibility` | `bool` | `true` when `EffectiveEnd < RequestedEnd` |
| `Justification` | `string` | 10-500 chars |
| `Status` | `EntitlementStatus` | see state machine |
| `Steps` | `IReadOnlyList<ApprovalStep>` | owned collection, ordered by `Order` |
| `CreatedAt`, `ApprovedAt`, `ClosedAt` | `DateTimeOffset`, `?`, `?` | |
| `Revocation` | `Revocation?` | owned; set once, **never overwrites** grant fields |

**Revocation** (owned): `RevokedById` (`PersonId?`, null for system), `RevokedAt`, `Reason` (1-500 chars, required), `Kind` (`Normal`, `Emergency`, `Termination`).

Entitlement state machine:
```
            ┌──────── Reject(step) ───────▶ Rejected (terminal)
PendingApproval ── Cancel(by requester) ──▶ Cancelled (terminal)
            └── Approve(last step) ──▶ Approved ── Revoke ──▶ Revoked (terminal)
                                         └── Expire (EffectiveEnd ≤ now) ──▶ Expired (terminal)
PendingApproval ── ExpireUnapproved (EffectiveEnd ≤ now, lifecycle run) ──▶ Expired (terminal)
```
- Requests do not time out while their window is still open. A request still `PendingApproval` when its `EffectiveEnd` has passed can no longer be approved (`approval.window_ended`): the lifecycle run calls `ExpireUnapproved(now)` (M11-17), which closes every `Pending`/`Waiting` step (`Closed`), sets `Status = Expired`, `ClosedAt = now`, raises `EntitlementExpired`; audit `request.expired_unapproved` (system `expiry`). The approval inbox also hides pending requests whose window has ended before that run. The requester sees "Expired before a decision".
- `Revoke` is allowed only from `Approved`; it records `Revocation` and creates `Revoke` fulfillments (M3+, rule in §2.5).
- `Cancel(PersonId by, now)` is allowed only from `PendingApproval` (`entitlement.invalid_transition`) and only by the requester (`by == RequestedById`, else `entitlement.cancel_forbidden`). It closes every `Pending`/`Waiting` step (`Closed`), sets `ClosedAt = now` and raises `EntitlementCancelled`.

**ApprovalStep** (owned by Entitlement)
| Field | Type | Rules |
|---|---|---|
| `Id` | `ApprovalStepId` | |
| `Order` | `int` | 1-based |
| `ApproverKind` | `ApproverKind` | `Manager`, `AreaOwner` |
| `AssignedApproverId` | `PersonId?` | set for `Manager` steps (resolved at request time); `null` for `AreaOwner` steps (any eligible owner) |
| `Status` | `ApprovalStepStatus` | `Waiting`, `Pending`, `Approved`, `Rejected`, `Closed` |
| `DecidedById`, `DecidedAt`, `Comment` | `PersonId?`, `DateTimeOffset?`, `string?` | `Comment` required (1-500) on reject |

Step rules:
- On creation: step 1 is `Pending`, the rest `Waiting`.
- Approving the `Pending` step makes it `Approved` and the next step `Pending`; approving the last step moves the Entitlement to `Approved`.
- Rejecting the `Pending` step makes it `Rejected`, all `Waiting` steps `Closed`, the Entitlement `Rejected`.
- Only the `Pending` step can be decided (`approval.step_not_pending` otherwise). Steps are never skipped.

**Approval routing** (`ApprovalRouter`, domain service):
- `Manager` → `[Manager]`; `AreaOwner` → `[AreaOwner]`; `Both` → `[Manager (1), AreaOwner (2)]`.
- **Manager step approver** (`ApproverResolver`, application service): `candidate = subject.ManagerId ?? subject.SponsorId`. If `candidate == RequestedById`, use `candidate`'s own `ManagerId` instead. If the result is `null`, the subject, or the requester → fail `approval.no_eligible_approver`.
- **Area owner step approvers:** all `Area.Owners` except the subject and the requester. If that set is empty → fail `approval.no_eligible_approver` at request time.
- **No self-approval:** the decider must not be the subject (`PersonId`) nor the requester (`RequestedById`) → `approval.self_approval_forbidden`. Enforced both in the domain (`Entitlement.Approve/Reject`) and by `NoSelfApprovalHandler` (see `authorization.md`).

**Access request policy** (`AccessRequestPolicy.Evaluate`, domain service, pure). Inputs: subject person + eligibility, area + security level, whether the area has ≥ 1 `AccessLevelMapping`, the subject's open entitlements for that area, requested start/end, justification, `now`. Checks in this order (first failure wins):
1. Subject `Status == Active` else `request.person_inactive`.
2. Justification 10-500 chars else `request.justification_invalid`.
3. `RequestedStart < RequestedEnd` else `request.invalid_window`.
4. `RequestedStart >= now - 5 minutes` else `request.start_in_past`.
5. `RequestedStart >= Eligibility.Start` else `request.before_eligibility`.
6. `Eligibility.End != null && RequestedStart >= Eligibility.End` → `request.outside_eligibility`.
7. `(RequestedEnd - RequestedStart).TotalDays <= MaxDurationDays` else `request.exceeds_max_duration`.
8. Area has ≥ 1 mapping else `request.area_not_mapped`.
9. No other entitlement for the same person + area with status `PendingApproval`, or `Approved` with `EffectiveEnd > now` → else `request.duplicate`.
10. Success: `EffectiveEnd = min(RequestedEnd, Eligibility.End)`, `CappedToEligibility` accordingly.

### 2.5 Fulfillment module

**PacsInstance** (aggregate)
| Field | Type | Rules |
|---|---|---|
| `Id` | `PacsInstanceId` | |
| `Name` | `string` | e.g. `HQ-Copenhagen` |
| `Vendor` | `PacsVendor` | `LegacyPacs`, `CloudPacs` |
| `SiteIds` | `IReadOnlyCollection<SiteId>` | sites covered; stored as `jsonb` array |
| `Capabilities` | `PacsCapabilities?` | last reported (see `adapter-interface.md`); `jsonb` |
| `LastHeartbeatAt` | `DateTimeOffset?` | |
| `AgentVersion` | `string?` | |
| `FulfillmentMode` | `FulfillmentMode` | `Manual` (staff console, M3) or `Connector` (agent, M4); `SetFulfillmentMode(mode)`. Seed: nordlys `HQ-Copenhagen` (LegacyPACS agent) and, from M6, nordlys `Warehouse-Aarhus` (CloudPACS agent) = `Connector`; all others (bricks `Berlin-Office`) `Manual` |

Connector **online** ⇔ `LastHeartbeatAt != null && now - LastHeartbeatAt <= 90 s` (from M4; before M4 `StaticConnectorHealthReader` reports all instances online). From M4 `PacsInstanceConnectorHealthReader`: `Manual` instances always count as online; `Connector` instances use `IsOnline(now)`.
`RecordHeartbeat(at, agentVersion, capabilitiesJson)` (M4, called by the gateway) raises `ConnectorCameOnline` when the instance was offline or never seen; returns whether capabilities changed.
In `Connector` mode staff `start`/`confirm`/`fail` return `fulfillment.connector_managed`; `retry` is allowed in both modes.

**AccessLevelMapping**: `Id`, `AreaId`, `PacsInstanceId`, `VendorAccessLevelRef` (`string`, e.g. `AL-CPH-SERVER`), `DisplayName`. Unique `(AreaId, PacsInstanceId)`. Seeded refs: LegacyPACS `AL-{areaCode}`, CloudPACS `AG-{areaCode}` (M6; CloudPACS calls them access groups).

**Capability gaps** (M6): the platform derives the list of missing capabilities of a `Connector`-mode instance from its stored `Capabilities` (`adapter-interface.md` §6). Unknown capabilities (never reported) = no list (`null`), and every feature counts as supported.

**Fulfillment** (aggregate, versioned)
| Field | Type | Rules |
|---|---|---|
| `Id` | `FulfillmentId` | |
| `EntitlementId` | `EntitlementId` | |
| `PacsInstanceId` | `PacsInstanceId` | |
| `Kind` | `FulfillmentKind` | `Grant`, `Revoke` |
| `Priority` | `CommandPriority` | `Normal`, `High` (emergency revocation); staff queue and connector commands serve `High` first |
| `Status` | `FulfillmentStatus` | `Desired`, `Pending`, `Confirmed`, `Failed`, `Drift`, `Superseded`; see state machine |
| `Revision` | `int` | starts at 1; +1 on each staff retry (feeds the idempotency key) |
| `CommandId` | `CommandId?` | current connector command (M4) |
| `Attempts` | `int` | delivery attempts of the current revision |
| `LastError` | `string?` | |
| `PacsRef` | `string?` | vendor reference returned in the ack |
| `ConfirmedByKind` | `ConfirmerKind?` | `Agent`, `Staff` |
| `ConfirmedById` | `string?` | agent: `ConnectorId`; staff: `PersonId` |
| `ConfirmedAt` | `DateTimeOffset?` | **server clock** at confirmation |
| `CreatedAt`, `UpdatedAt` | `DateTimeOffset` | |

One fulfillment per `(EntitlementId, PacsInstanceId, Kind)`. On approval, one `Grant` fulfillment is created per `AccessLevelMapping` of the area (status `Desired`).

**On revocation** (`FulfillmentPlanner.PlanRevocationAsync`, M3), per `Grant` fulfillment of the entitlement:
| Grant status | Effect |
|---|---|
| `Desired` (never dispatched) | `Supersede`; no `Revoke` needed |
| `Pending`, `Failed` (may be partly applied) | `Supersede` **and** create a `Revoke` for that instance |
| `Confirmed`, `Drift` | create a `Revoke` for that instance (M11-16: one `Revoke` per entitlement and instance; if a mapping edit already used it, it is re-applied, or retried when `Failed`) |

New `Revoke` fulfillments start `Desired` with priority `High` for `Emergency` revocations, else `Normal`. If no `Revoke` is created, the revocation is confirmed immediately.

Fulfillment state machine:
```
Desired ──Dispatch (command handed to agent / staff starts)──▶ Pending   (MarkPending(commandId) raises FulfillmentDispatched when commandId != null)
Pending ──Confirm (ack Applied / staff confirm)──▶ Confirmed
Pending ──Fail (non-retryable, or retries exhausted / staff fail)──▶ Failed
Desired ──Confirm/Fail by staff (manual mode, M3)──▶ Confirmed | Failed
Failed ──Retry (staff; Revision+1)──▶ Desired
Confirmed ──DetectDrift (reconciliation / live drift)──▶ Drift
Drift ──Remediate (re-push; Revision+1)──▶ Desired
Drift ──ResolveAsInSync (staff verified)──▶ Confirmed
{Confirmed, Pending} ──Reapply(priority) (mapping edit moves a grant / re-opens its revoke, M11-16; Revision+1, priority only rises)──▶ Desired
{Desired, Pending, Failed} ──Supersede (entitlement revoked before the grant was confirmed)──▶ Superseded (terminal)
```
`FlagRevocationUnconfirmed(revokedAt, now)` (M3) changes no state: allowed for `Revoke` fulfillments that are not `Confirmed`/`Superseded`; raises `RevocationUnconfirmedAlert`. Any other call returns `fulfillment.invalid_transition`.
**Entitlement is "confirmed"** ⇔ status `Approved` and **every** `Grant` fulfillment is `Confirmed`. **Revocation is "confirmed"** ⇔ every `Revoke` fulfillment is `Confirmed`.

**ConnectorCommand** (M4, aggregate in `Gatehouse.Domain.Connectors`, row in `connector_commands`; written in the same transaction as the domain change, ADR-005)
| Field | Type |
|---|---|
| `Id` | `CommandId` |
| `PacsInstanceId` | `PacsInstanceId` |
| `Type` | `string` (command name from `messages.md`) |
| `PartitionKey` | `string` (`person:{personId}` or `door:{doorId}`) |
| `Sequence` | `long` (per `(PacsInstanceId, PartitionKey)`, from `connector_partition_sequences`) |
| `Priority` | `CommandPriority` (`Normal`, `High`) |
| `IdempotencyKey` | `string` |
| `Payload` | `jsonb` |
| `Status` | `Queued`, `Delivered`, `Acked`, `Failed`, `Superseded` |
| `Attempts`, `NextAttemptAt`, `LeaseUntil`, `DeliveredAt`, `AckedAt`, `LastError` | |
| `FulfillmentId` | `FulfillmentId?` |

Command status transitions (`ConnectorCommandStatus`): `Queued ──Deliver──▶ Delivered`; `Delivered ──ExpireLease──▶ Queued` (or `Failed` with `connector.lease_expired` when `Attempts >= 5`); `Delivered|Queued(Attempts>0) ──ApplyAck──▶ Acked | Queued (retryable, backoff) | Failed`; `Queued ──Supersede──▶ Superseded`. `Acked`, `Failed`, `Superseded` are terminal. Rules: `connector-protocol.md` §3.
`connector_partition_sequences(pacs_instance_id, partition_key, last_sequence)` PK `(pacs_instance_id, partition_key)`; next sequence = `INSERT ... ON CONFLICT DO UPDATE SET last_sequence = last_sequence + 1 RETURNING last_sequence` in the command's transaction (gaps are harmless).

### 2.6 Live building module (M5)

**DoorEvent** (partitioned table, ADR-006): `Id` (`Guid`, agent `eventId`), `OccurredAt` (PACS time), `ReceivedAt` (server time), `PacsInstanceId`, `DoorId?`, `ReaderId?`, `PacsDoorRef`, `PacsReaderRef?`, `Type` (`DoorEventType`), `PersonId?`, `CardNumberHash?` (HMAC-SHA-256 hex with a per-tenant key, PT-34; rows before M11-27 hold unkeyed SHA-256; raw number never stored), `Direction?`.

`DoorEventType`: `AccessGranted`, `AccessDenied`, `DoorOpened`, `DoorClosed`, `DoorHeldOpen`, `DoorForced`, `LockdownStarted`, `LockdownCleared`, `ScheduledUnlockStarted`, `ScheduledUnlockEnded`, `DoorUnlocked`, `DoorRelocked`.

**DoorState** (Redis only, key `tenant:{tenantId}:door:{doorId}:state`): `Locked`, `Unlocked`, `Open`, `Closed`, `HeldOpen`, `ForcedOpen`, `Lockdown`, `ScheduledUnlocked`. Transition function `DoorStateMachine.Apply(DoorState current, DoorEventType evt) → DoorState`:

| From | Event | To |
|---|---|---|
| Locked | AccessGranted / DoorUnlocked | Unlocked |
| Locked | AccessDenied | Locked |
| Unlocked | DoorOpened | Open |
| Unlocked | DoorRelocked (unlock timeout, not opened) | Locked |
| Open | DoorClosed | Closed |
| Open | DoorHeldOpen (open > 30 s) | HeldOpen |
| HeldOpen | DoorClosed | Closed |
| Closed | DoorRelocked | Locked |
| Locked | DoorOpened (no grant) / DoorForced | ForcedOpen |
| ForcedOpen | DoorClosed | Closed |
| any | LockdownStarted | Lockdown |
| Lockdown | LockdownCleared | Locked |
| Locked | ScheduledUnlockStarted | ScheduledUnlocked |
| ScheduledUnlocked | ScheduledUnlockEnded | Locked |

Any other pair leaves the state unchanged and logs at Debug. `Lockdown` ignores every event except `LockdownCleared` (events are still stored).

**Alarm**: `Id`, `DoorId`, `Kind` (`Forced`, `HeldOpen`, `UnauthorizedAccess` (M7, live drift, §2.7)), `RaisedAt`, `DoorEventId`, `AcknowledgedById?`, `AcknowledgedAt?`, `Comment?` (required to acknowledge).

M5 details (table names snake_case; ids `AlarmId`, `DoorCommandId` are `Guid` value objects like the others):
- `door_events` columns as above; `Id` is the agent `eventId`; PK `(occurred_at, id)` (ADR-006). Rows whose `PacsDoorRef` matches no door are stored with `DoorId = null`.
- `alarms`: at most one **open** alarm (`AcknowledgedAt IS NULL`) per `(DoorId, Kind)`. `Alarm.Raise(doorId, kind, doorEventId, now)` raises `AlarmRaised`; `Acknowledge(personId, comment, now)` (comment 1-500) raises `AlarmAcknowledged`. Alarm kind for an event: `DoorForced`, or `DoorOpened` while `Locked` → `Forced`; `DoorHeldOpen` → `HeldOpen`.
- **DoorCommand** (aggregate, table `door_commands`): `Id` (`DoorCommandId`), `Kind` (`DoorCommandKind`: `MomentaryUnlock`, `LockdownStart`, `LockdownClear`), `Scope` (`DoorCommandScope`: `Door`, `Area`, `Floor`, `Site`), `ScopeId` (`Guid`), `RequestedById` (`PersonId`), `Reason` (1-500), `DurationSeconds?` (1-60, `MomentaryUnlock` only, which also requires scope `Door`), `DoorCount`, `RequestedAt`. `Request(...)` raises `DoorCommandRequested`. One `ConnectorCommand` per connector door: partition `door:{doorId}`, priority `High`, idempotency key `{type}:{doorCommandId}:{doorId}`, no `FulfillmentId`.
- `PacsInstance.OfflineNotifiedAt` (`DateTimeOffset?`): `MarkOfflineIfStale(now) → bool` raises `ConnectorWentOffline` once for a `Connector`-mode instance with a heartbeat that is no longer online; `RecordHeartbeat` clears it.
- Redis (not tables): door state `tenant:{tenantId}:door:{doorId}:state`; connector health `tenant:{tenantId}:connector:{pacsInstanceId}:health` (`agentVersion`, `queueDepth`, `pacsReachable`, `receivedAt`); live updates channel `tenant:{tenantId}:live`.

### 2.7 Reconciliation module (M7)

Namespace `Gatehouse.Domain.Reconciliation`; ids `ReconciliationReportId`, `ReconciliationFindingId` (`Guid` value objects, `New()` = v7). Only `Connector`-mode instances are reconciled (staff-managed instances have no agent to export).

**ReconciliationReport** (aggregate, versioned, table `reconciliation_reports`)
| Field | Type | Rules |
|---|---|---|
| `Id` | `ReconciliationReportId` | also the `ExportId` of the `ExportRecords` command (`exportId == reportId`) |
| `PacsInstanceId` | `PacsInstanceId` | |
| `RequestedById` | `PersonId?` | `null` = scheduler |
| `RequestedAt` | `DateTimeOffset` | |
| `Status` | `ReconciliationStatus` | `Requested → Exporting → Comparing → Completed`; `Requested\|Exporting\|Comparing → Failed` |
| `CommandId` | `CommandId` | the `ExportRecords` connector command |
| `ReceivedChunkCount` | `int` | next expected `chunkIndex` |
| `CapturedAt` | `DateTimeOffset?` | PACS export time (`capturedAt` of chunk 0) |
| `ComparedAt` | `DateTimeOffset?` | server clock when the comparison finished |
| `Coverage` | `ReconciliationCoverage?` (`jsonb`) | set on completion, see below |
| `FailureCode`, `FailureMessage` | `string?` | on `Failed` |

`ReconciliationCoverage`: `SiteIds` (`Guid[]`, the instance's sites), `AreaIds` (`Guid[]`, areas mapped to the instance), `VendorAccessLevelRefs` (`string[]`, export scope), `RecordTypes` (`string[]`, always `["AccessAssignment"]` in M7), `ExportedRecordCount`, `ComparedRecordCount`, `InSyncCount`, `SkippedInFlightCount`, `IgnoredInactiveCount` (all `int`).

Methods (each returns `Result`; wrong state → `reconciliation.invalid_transition`): `Request(instanceId, requestedById?, commandId, now)` raises `ReconciliationRequested`; `AcceptChunk(chunkIndex, capturedAt, isLast)` → `ChunkAcceptance` (`Accepted`, `Duplicate`, `OutOfOrder`; `Requested → Exporting` on the first chunk, `→ Comparing` on the last); `Complete(coverage, findingCount, now)` raises `ReconciliationCompleted`; `Fail(code, message)`.

**ReconciliationExportRecord** (table `reconciliation_export_records`, rows staged by the gateway, deleted with the report): `ReportId`, `ChunkIndex`, `Ordinal`, then the `PacsRecord` fields (`RecordType`, `PacsRef`, `PersonRef?`, `CardNumber?`, `VendorAccessLevelRef?`, `ValidFrom?`, `ValidTo?`, `IsActive`). PK `(report_id, chunk_index, ordinal)`. `CardNumber` is the full number (like `cards.card_number`); it never leaves the database in DTOs, messages, logs or audit (mask with `CardNumberMask.Mask`).

**ReconciliationFinding** (table `reconciliation_findings`): `Id`, `ReportId`, `Kind` (`FindingKind`: `MissingInPacs`, `ExtraInPacs`, `Mismatch`, `ExpiredStillPresent`), `PersonId?`, `AreaId?`, `EntitlementId?`, `FulfillmentId?` (the `Grant` fulfillment it is about), `PacsRef?`, `PersonRef?`, `VendorAccessLevelRef`, `PacsCardNumber?` (full, needed to remove the assignment; never in DTOs), `Details` (`jsonb`: `differences` (`string[]` of `cardNumber`, `validFrom`, `validTo`), `expected`/`actual` objects with `cardNumberMasked`, `validFrom`, `validTo`), `StaffTaskId`, `CreatedAt`. Inactive/history PACS rows never produce findings.

**Comparison** (`ReconciliationComparer.Compare(ReconciliationInput) → ReconciliationOutcome`, pure domain service). Key = `(PersonRef, VendorAccessLevelRef)`; `PersonRef` is the `PersonId` string. Only `AccessAssignment` records whose `VendorAccessLevelRef` is in the export scope are compared.
- A PACS row is **active** ⇔ `IsActive && (ValidTo == null || ValidTo > CapturedAt)`; other rows are counted in `IgnoredInactiveCount` and never compared.
- **Expected** = `Grant` fulfillments of the instance with status `Confirmed` or `Drift` whose entitlement is `Approved` with `EffectiveEnd > CapturedAt`.
- **Expired** = `Grant` fulfillments with status `Confirmed` or `Drift` whose entitlement is `Approved`/`Expired` with `EffectiveEnd <= CapturedAt`.
- **PACS window** (lab finding #1): a PACS holds one assignment per person and access level, so the expected window of a key is `AccessWindow.Current` over its live approved windows: the window that ends first, merged with every window that overlaps or touches it. `DispatchFulfillmentHandler` writes the same window on `GrantAccess`; a `Revoke` while another live entitlement shares the level re-grants that remaining window instead of `RevokeAccess`.
- **In flight** keys (never findings, counted in `SkippedInFlightCount`): a `Grant` fulfillment `Desired`/`Pending`/`Failed` of an `Approved` entitlement, or a `Revoke` fulfillment that is not `Confirmed`/`Superseded`.
- Expected without an active row → `MissingInPacs`. Expected with an active row: `Mismatch` if the row's `CardNumber` is not one of the person's `Active` cards, or (only when the instance reports `NativeValidity`) `ValidFrom`/`ValidTo` differ from `RequestedStart`/`EffectiveEnd` by more than 1 s; else in sync.
- Active row with no expected key: in flight → skipped; an expired key → `ExpiredStillPresent`; else → `ExtraInPacs` (`PersonId` set when the ref is a known person).
- `ComparedRecordCount` = expected keys + active rows not matched to an expected key and not skipped; "in sync" is only ever reported for compared records.

**Completion** (`CompareReconciliationHandler`, one transaction): stores findings; `MissingInPacs`/`Mismatch` call `Fulfillment.DetectDrift(findingId, now)` on a `Confirmed` grant (a grant already in `Drift` is left as is); each finding gets a `StaffTask(ReconciliationFinding, area's site, "ReconciliationFinding", findingId)` unless an unresolved `ReconciliationFinding` task exists for an earlier finding with the same `(PacsInstanceId, Kind, PersonRef, VendorAccessLevelRef)`, whose task id is then reused; raises `ReconciliationFindingCreated` per finding; audit `reconciliation.completed`.

**Remediation** (resolving a `ReconciliationFinding` task, `POST /api/v1/tasks/{id}/resolve`):
| Finding kind | Allowed actions | Effect |
|---|---|---|
| `MissingInPacs`, `Mismatch` | `Remediated` | `Fulfillment.Remediate` (Drift → Desired, `Revision + 1`, re-pushed by the dispatcher) |
| | `VerifiedInPacs` | `Fulfillment.ResolveAsInSync` |
| | `Dismissed` (note required) | nothing else |
| `ExtraInPacs`, `ExpiredStillPresent` | `RemovedFromPacs` | queues a `RevokeAccess` connector command (no fulfillment; partition `person:{id}` for a known person else `finding:{findingId}`; idempotency key `RevokeAccess:{findingId}:1`; `Kind = Standard`) |
| | `Dismissed` (note required) | nothing else |
A `DriftAlert` task allows `VerifiedInPacs` or `Dismissed` (note required). Not allowed / missing note → `task.resolution_required`. Every remediation is audited (`reconciliation.remediated`).

**Live drift** (M7): when an `AccessGranted` door event at a known door is ingested, the person is **authorized** if they have an entitlement for the door's area that is `Approved` with `RequestedStart <= OccurredAt < EffectiveEnd` and whose `Grant` fulfillments for the event's instance are all `Confirmed` or `Pending`, or that is `Revoked` with a `Revoke` fulfillment not yet `Confirmed`. Otherwise (including an unknown person) `Alarm.RaiseUnauthorizedAccess(doorId, areaId, personId?, doorEventId, now)` raises an `UnauthorizedAccess` alarm (`AlarmRaised` + `LiveDriftAlertRaised`), subject to the one-open-alarm-per-`(DoorId, Kind)` rule, and opens a `StaffTask(DriftAlert, site, "Alarm", alarmId)`.

### 2.8 Tasks and audit

**StaffTask** (aggregate, versioned, `Gatehouse.Domain.Tasks`, M3+)
| Field | Type | Rules |
|---|---|---|
| `Id` | `StaffTaskId` | |
| `Kind` | `StaffTaskKind` | `FulfillmentFailed`, `RevocationUnconfirmed` (M3); `ReconciliationFinding`, `DriftAlert` (M7); `PinReveal` (M8); `ScheduledRemovalRefused` (M11: subject `ScheduledRemoval:{connectorId:N}` + a Guid derived from (connector, `revocationRef`), opened and auto-resolved from the agent heartbeat, `connector-protocol.md` §2.5) |
| `SiteId` | `SiteId?` | site of the subject (area's site); `null` = tenant-wide (only tenant-wide staff handle it) |
| `SubjectType`, `SubjectId` | `string`, `Guid` | e.g. `Fulfillment` + fulfillment id |
| `AssignedToId` | `PersonId?` | set by `Claim` |
| `Status` | `StaffTaskStatus` | `Open → InProgress → Resolved` (terminal); `Open → Resolved` also allowed |
| `ResolutionAction` | `StaffTaskResolutionAction?` | `AutoResolved` (system: subject confirmed/superseded), `Retried`, `ConfirmedManually`, `VerifiedInPacs`, `Dismissed`; M7: `Remediated`, `RemovedFromPacs` (allowed per kind, §2.7) |
| `ResolutionNote` | `string?` | ≤ 500 |
| `ResolvedById`, `ResolvedAt` | `PersonId?`, `DateTimeOffset?` | `ResolvedById` null when resolved by the system |
| `CreatedAt`, `UpdatedAt` | `DateTimeOffset` | |

Rules: `Open(kind, siteId, subjectType, subjectId, now)` raises `StaffTaskCreated`. `Claim(by, now)` → `InProgress`, `AssignedToId = by` (re-claim by another person takes over). `Resolve(by?, action?, note?, now)` requires an action (`task.resolution_required`), raises `StaffTaskResolved`. Claim/resolve on a `Resolved` task → `task.invalid_transition`. At most **one unresolved task per `(Kind, SubjectId)`** (filtered unique index). In M3 tasks are opened/auto-resolved in-process by `StaffTaskService` (ADR-008).

**AuditEntry** (M3, append-only, `Gatehouse.Domain.Audit`): `Sequence` (`bigint` PK, **assigned by the writer** as last + 1 under the advisory lock - not an identity, so the chain has no gaps and the hash can include it), `OccurredAt`, `ActorKind` (`Person`, `Agent`, `System`), `ActorId` (`string`: person id, connector id, or system component name such as `revocation-monitor`), `Action` (dotted, `^[a-z]+(\.[a-z_]+)+$`), `SubjectType`, `SubjectId` (`string`), `Payload` (`jsonb`, never contains PINs, tokens or full card numbers), `PrevHash` (`char(64)`), `Hash` (`char(64)`).
- `OccurredAt` is truncated to whole **microseconds** (Postgres `timestamptz` precision) when the entry is created, so the hash survives the round trip.
- `Hash = lowercase hex( SHA256( UTF8( PrevHash + "\n" + CanonicalJson ) ) )` where `CanonicalJson` is the compact JSON object `{sequence (number), occurredAt (string "yyyy-MM-ddTHH:mm:ss.fffffffZ" in UTC), actorKind, actorId, action, subjectType, subjectId, payload}` with properties in that order; `payload` is canonicalized: object keys sorted ordinally at every level, arrays in order, no whitespace. Payload values are strings, integers, booleans, null, arrays or objects of those (no floating point: `jsonb` may reformat it). Reference vector: `AuditHasherTests.Known_vector_matches` (M3-03).
- Genesis `PrevHash` = 64 × `0`.
- Writers (handlers) call `IAuditLog.Record(...)` before their single save; `TenantDbContext.SaveChangesAsync` begins a transaction if none, takes `pg_advisory_xact_lock(7340001)`, reads the last `(sequence, hash)`, seals the pending entries in creation order, saves and commits - all in the business transaction.
- Append-only is enforced in the database: triggers `audit_entries_no_update_delete` (BEFORE UPDATE OR DELETE, per row) and `audit_entries_no_truncate` raise an exception; `REVOKE UPDATE, DELETE, TRUNCATE ON audit_entries FROM PUBLIC`. (A dedicated least-privilege app role with only `INSERT, SELECT` is a M9 hardening item.)
- Verification (`GET /api/v1/audit/verify`) walks the chain in sequence order and reports the first sequence whose number, `PrevHash` or `Hash` does not match.

**Audit actions (M3)** - recorded by the named handler in the same transaction:
| Action | Actor | Subject | Payload |
|---|---|---|---|
| `entitlement.requested` | requester | Entitlement | `personId`, `areaId`, `requestedStart`, `requestedEnd`, `effectiveEnd` (ISO strings), `cappedToEligibility` |
| `approval.granted` | decider | Entitlement | `stepId`, `approverKind`, `comment` |
| `approval.rejected` | decider | Entitlement | `stepId`, `approverKind`, `comment` |
| `entitlement.approved` | final approver | Entitlement | `fulfillmentIds` (array) |
| `entitlement.cancelled` | requester | Entitlement | `{}` |
| `entitlement.revoked` | revoker | Entitlement | `reason`, `kind`, `revokeFulfillmentIds` (array) |
| `entitlement.expired` | system `expiry` | Entitlement | `revokeFulfillmentIds` (array): expiry plans a `Revoke` per settled grant, like termination |
| `request.expired_unapproved` | system `expiry` | Entitlement | `effectiveEnd` (ISO): a pending request whose window ended (M11-17) |
| `person.terminated_by_admin` | tenant admin | Person | `by` (followed by `person.terminated`) |
| `fulfillment.started` | staff | Fulfillment | `entitlementId`, `kind`, `revision` |
| `fulfillment.confirmed` | staff (agent in M4) | Fulfillment | `entitlementId`, `kind`, `revision`, `confirmedAt` |
| `fulfillment.failed` | staff (agent in M4) | Fulfillment | `entitlementId`, `kind`, `revision`, `errorCode`, `reason` |
| `fulfillment.retried` | staff | Fulfillment | `entitlementId`, `kind`, `revision` (the new one) |
| `fulfillment.superseded` | revoker | Fulfillment | `entitlementId`, `kind` |
| `revocation.unconfirmed_alert` | system `revocation-monitor` | Fulfillment | `entitlementId`, `outstandingMinutes` |
| `task.created` | person or system | StaffTask | `kind`, `subjectType`, `subjectId` |
| `task.claimed` | staff | StaffTask | `{}` |
| `task.resolved` | staff or system | StaffTask | `action`, `note` |
| `task.removal_refused_reported` | agent | StaffTask | `revocationRef`, `code`, `firstRefusedAt`, `attempts` (M11-15) |
| `pacs_instance.grants_migrated` | tenant admin | PacsInstance | `areas`, `grants` (counts only; a mapping edit that changed a level moved active grants, M11-16) |
| `fulfillment.migration_planned` | tenant admin | Fulfillment (the revoke) | `entitlementId`, `fromLevel`, `fromDisplayName`, `toLevel`: the level this revoke removes (M11-16) |
| `fulfillment.migration_grant_planned` | system `dispatcher` / staff | Fulfillment (the grant) | `entitlementId`, `revokeFulfillmentId`, `revision`: grant re-applied with the new level after the revoke went out (M11-16) |

**Audit actions (M4):**
| Action | Actor | Subject | Payload |
|---|---|---|---|
| `connector.registered` | system `cli` / `bootstrap` | PacsInstance | `connectorId`, `displayName` |
| `connector.enrolled` | agent | PacsInstance | `connectorId`, `certificateThumbprint`, `notAfter` |
| `fulfillment.confirmed` / `fulfillment.failed` | agent (`AuditActor.ForAgent(connectorId)`) | Fulfillment | as in M3; `errorCode` is the ack error code |

**Audit actions (M5):**
| Action | Actor | Subject | Payload |
|---|---|---|---|
| `door.command_requested` | staff | DoorCommand | `kind`, `scope`, `scopeId`, `reason`, `durationSeconds`, `doorCount` |
| `alarm.acknowledged` | staff | Alarm | `kind`, `doorId`, `comment` |

**Audit actions (M7):**
| Action | Actor | Subject | Payload |
|---|---|---|---|
| `reconciliation.requested` | staff or system `reconciliation-scheduler` | ReconciliationReport | `pacsInstanceId`, `commandId` |
| `reconciliation.completed` | system `reconciliation` | ReconciliationReport | `comparedRecordCount`, `inSyncCount`, `findingCount` |
| `reconciliation.failed` | system `reconciliation` | ReconciliationReport | `code`, `message` |
| `reconciliation.remediated` | staff | ReconciliationFinding | `kind`, `action`, `fulfillmentId` (or null), `commandId` (or null) |
| `drift.live_alert_raised` | system `live-drift` | Alarm | `doorId`, `areaId`, `personId` (or null), `doorEventId` |

Later milestones add their actions here (card activation, PIN reveal, admin changes, sign-ins).

### 2.9 Messaging tables (ADR-005)

- `outbox_messages`: `id uuid PK`, `occurred_at`, `type` (message name), `payload jsonb`, `priority` (`Normal`/`High`), `attempts int`, `next_attempt_at`, `dispatched_at?`, `last_error?`.
- `processed_messages`: `(message_id uuid, consumer text)` PK, `processed_at`.

### 2.10 Tenant onboarding and admin (M10)

- **Default baseline** written by provisioning step `Baseline`: the four security levels of §2.1 and no other rows. Security level CRUD stays in M12.
- **First tenant admin** (step `FirstAdmin`): `Person(Source = Authoritative, Type = Employee, Eligibility open-ended from now in TimeZoneId)` + `RoleAssignment(TenantAdmin)`. Admin-created employees (`POST /api/v1/admin/people/employees`, CSV import) use the same source; HR sync later matches them by email.
- **Identity provider summary** in `tenant_settings` key `identity.provider` (JSON, **no secrets**; the client secret lives only in Keycloak): `TenantIdentityProvider(IdpKind Kind, string Alias, string DisplayName, string? DiscoveryUrl, string? Issuer, string? ClientId, string? SamlEntityId, string? SamlSsoUrl, string? SigningCertificateThumbprint, DateTimeOffset SavedAt, DateTimeOffset? LastTestedAt, bool? LastTestOk)`. Saving also sets the catalog `Tenant.IdpKind` (`Local` when removed).
- **Floor-plan geometry rules for admin edits** (in addition to §2.1): area polygons may not overlap another area's polygon on the same floor by more than 1 % of the smaller area (`area.overlaps`); doors and readers `Position` inside the floor plan (`door.invalid_position`, `reader.invalid_position`); shrinking a floor plan below an existing polygon or door → `floor.invalid_plan_size`.
- **Floor background** (M10): customer drawing uploaded by the tenant admin; JPEG/PNG sniffed, ≤ 10 MiB before decode, decoded ≤ 8192 px, re-encoded ≤ 4096 px without metadata; object key `tenant/{tenantId}/floors/{floorId}/{guid}.{ext}`; deleting the floor deletes the object. Not personal data (no retention rule).
- **Deletion rules:** site with floors or covered by a PACS instance → `site.not_empty`; floor with areas → `floor.not_empty`; area referenced by any entitlement (any status) or access-level mapping → `area.in_use`; PACS instance with a non-revoked connector registration or any fulfillment → `pacs.in_use`. Doors and readers can always be deleted (door events keep their `PacsDoorRef`).
- **Onboarding settings** in `tenant_settings`: `onboarding.skipped.{step}` (ISO instant), `onboarding.completed_at`. Steps (`OnboardingStep`): `IdentityProvider` (an IdP saved and last test ok), `Sites` (≥ 1 site with a floor and ≥ 1 area with a door), `People` (≥ 1 Employee besides the first admin and every area has an owner), `Pacs` (≥ 1 instance and every area on its sites mapped), `Connector` (every `Connector`-mode instance has an `Active` registration that is online; skippable = stay `Manual`), `Staff` (≥ 1 `SecurityStaff` assignment; skippable). State per step: `NotStarted`, `InProgress`, `Complete`, `Skipped`; computed on read.
- **Audit actions (tenant chain):** `identity_provider.saved`, `identity_provider.tested`, `identity_provider.removed`, `site.created`, `site.updated`, `site.deleted`, `floor.created`, `floor.updated`, `floor.deleted`, `floor.background_changed`, `floor.background_removed`, `area.created`, `area.updated`, `area.deleted`, `area.owners_changed`, `door.created`, `door.updated`, `door.deleted`, `reader.created`, `reader.updated`, `reader.deleted`, `person.created`, `people.imported`, `role.assigned`, `role.removed`, `pacs_instance.created`, `pacs_instance.updated`, `pacs_instance.deleted`, `pacs_instance.mappings_changed`, `pacs_instance.grants_migrated`, `connector.registered`, `connector.revoked`, `onboarding.step_skipped`, `onboarding.completed`. Provisioning writes `tenant.provisioned` (system `platform`) as the first entry of a new tenant chain.

---

## 3. Area access state (map, spec §5.2)

`AreaAccessStateCalculator.Calculate(AreaAccessInput input) → AreaAccessResult` (pure domain service in `Gatehouse.Domain.Access`).

`AreaAccessInput`: `Now`, `Eligibility` (`DateRange`), `PersonActive` (`bool`), `AreaMapped` (`bool`), `OfflineSince` (`DateTimeOffset?`, earliest `LastHeartbeatAt` among offline mapped instances, `null` = all online), `AnyConnectorOffline` (`bool`), `Entitlements` (list of `{ Status, RequestedStart, EffectiveEnd, AllGrantsConfirmed, RevokePendingConfirmation }`), `HasActiveCard` (`bool?`, `null` = unknown/not tracked yet).

`AreaAccessResult`: `State` (`AreaAccessState`: `HasAccess`, `NoAccess`, `Pending`, `Unknown`), `Reason` (`AreaAccessReason`), `CardInactive` (`bool`), `Since` (`DateTimeOffset?`).

Algorithm (first match wins):
1. `!AreaMapped` → `Unknown`, `AreaNotMapped`.
2. `AnyConnectorOffline` → `Unknown`, `ConnectorOffline`, `Since = OfflineSince`.
3. Exists entitlement with `Status == Approved && AllGrantsConfirmed && RequestedStart <= Now < EffectiveEnd` **and** `PersonActive && Eligibility.Contains(Now)` → `HasAccess`, `Confirmed`; `CardInactive = HasActiveCard == false`.
4. Exists `Revoked` entitlement with `RevokePendingConfirmation` → `Pending`, `RevocationPending`.
5. Exists `PendingApproval` → `Pending`, `AwaitingApproval`.
6. Exists `Approved && !AllGrantsConfirmed && EffectiveEnd > Now` → `Pending`, `AwaitingFulfillment`.
7. Exists `Approved && AllGrantsConfirmed && Now < RequestedStart` → `Pending`, `ScheduledStart`.
8. Exists `Approved && AllGrantsConfirmed` (window or eligibility not satisfied) → `NoAccess`, `OutsideWindow`.
9. Otherwise → `NoAccess`, `NoEntitlement`.

`AreaAccessReason`: `AreaNotMapped`, `ConnectorOffline`, `Confirmed`, `RevocationPending`, `AwaitingApproval`, `AwaitingFulfillment`, `ScheduledStart`, `OutsideWindow`, `NoEntitlement`.

UI labels are fixed (cannot be themed): `HasAccess` → "Has access" ✓, `NoAccess` → "No access" ✕, `Pending` → "Pending" ⏳ (hatched), `Unknown` → "Unknown" ?. `CardInactive` adds the note "assigned access; card inactive".

---

## 4. Error codes

Format `{module}.{snake_case}`. HTTP status comes from `ErrorKind` (see `CLAUDE.md`). New codes are added here first.

| Code | Kind | Raised by |
|---|---|---|
| `tenant.invalid_id` | Validation | `TenantId.Create` |
| `tenant.missing_claim` | Forbidden | tenant resolution |
| `tenant.unknown` | Forbidden | tenant resolution |
| `tenant.inactive` | Forbidden | tenant resolution |
| `tenant.already_exists` | Conflict | `tenant create` |
| `tenant.invalid_display_name` | Validation | `Tenant.Create` (display name 1-100) |
| `site.invalid_name`, `site.invalid_code` | Validation | Site |
| `floor.invalid_name`, `floor.invalid_level` | Validation | Floor |
| `area.invalid_name`, `area.invalid_code` | Validation | Area |
| `door.invalid_name`, `reader.invalid_name` | Validation | Door, Reader |
| `security_level.invalid_name`, `security_level.invalid_rank`, `security_level.invalid_max_duration` | Validation | SecurityLevel |
| `person.invalid_name`, `person.invalid_email` | Validation | Person |
| `pacs.invalid_name` | Validation | PacsInstance |
| `mapping.invalid` | Validation | AccessLevelMapping |
| `validation.failed` | Validation | FluentValidation request-shape failures (with `errors`) and invalid query parameters |
| `identity.person_not_provisioned` | Forbidden | current user resolution |
| `site.name_taken`, `site.code_taken`, `site.invalid_time_zone` | Conflict / Validation | Site |
| `floor.level_taken`, `floor.invalid_plan_size` | Conflict / Validation | Floor |
| `area.invalid_polygon` | Validation | Polygon / Area |
| `area.last_owner` | Conflict | Area.RemoveOwner |
| `area.owner_not_employee` | Validation | Area.AddOwner |
| `area.not_found` | NotFound | handlers |
| `floor.not_found`, `site.not_found`, `person.not_found` | NotFound | handlers |
| `person.local_requires_sponsor` | Validation | Person |
| `person.local_requires_end_date` | Validation | Person |
| `person.non_employee_must_be_local` | Validation | Person |
| `person.self_reference` | Validation | Person (manager/sponsor = self) |
| `person.sponsor_not_employee` | Validation | Person |
| `person.email_taken` | Conflict | handlers |
| `person.invalid_eligibility` | Validation | Eligibility (`End <= Start`, open end for non-authoritative) |
| `person.identity_already_linked` | Conflict | Person.LinkIdentity (different subject) |
| `role.tenant_admin_cannot_be_site_scoped` | Validation | RoleAssignment.Create |
| `request.person_inactive` | Validation | AccessRequestPolicy |
| `request.justification_invalid` | Validation | AccessRequestPolicy |
| `request.invalid_window` | Validation | AccessRequestPolicy |
| `request.start_in_past` | Validation | AccessRequestPolicy |
| `request.before_eligibility` | Validation | AccessRequestPolicy |
| `request.outside_eligibility` | Validation | AccessRequestPolicy |
| `request.exceeds_max_duration` | Validation | AccessRequestPolicy |
| `request.area_not_mapped` | Validation | AccessRequestPolicy |
| `request.duplicate` | Conflict | AccessRequestPolicy |
| `request.forbidden_for_subject` | Forbidden | `RequestAccessRequirement` |
| `approval.no_eligible_approver` | Validation | ApproverResolver |
| `approval.step_not_pending` | Conflict | Entitlement.Approve/Reject |
| `approval.self_approval_forbidden` | Forbidden | Entitlement / NoSelfApprovalHandler |
| `approval.window_ended` | Conflict | Entitlement.Approve: `EffectiveEnd <= now` (the request can still be rejected or cancelled) |
| `approval.not_an_approver` | Forbidden | `ApproveStepRequirement` |
| `approval.comment_required` | Validation | Entitlement.Reject |
| `approval.step_not_found` | NotFound | handlers |
| `entitlement.not_found` | NotFound | handlers (also when not visible to the caller) |
| `entitlement.invalid_transition` | Conflict | Entitlement (also concurrent update) |
| `entitlement.revocation_reason_required` | Validation | Entitlement.Revoke |
| `entitlement.cancel_forbidden` | Forbidden | Entitlement.Cancel (caller is not the requester) |
| `entitlement.revoke_forbidden` | Forbidden | `RevokeEntitlementRequirement` |
| `fulfillment.not_found` | NotFound | handlers (also when outside the staff member's scope) |
| `fulfillment.invalid_transition` | Conflict | Fulfillment (also concurrent update) |
| `fulfillment.reason_required` | Validation | fail fulfillment (reason 1-500) |
| `fulfillment.self_handling_forbidden` | Forbidden | `NoSelfFulfillmentHandler` (staff is subject or requester) |
| `card.invalid_transition` | Conflict | Card |
| `card.invalid_number` | Validation | Card.Create |
| `fulfillment.connector_managed` | Conflict | staff start/confirm/fail on a `Connector`-mode instance |
| `fulfillment.no_active_card` | (fail code) | dispatch: person has no usable card; fulfillment → `Failed` |
| `fulfillment.no_access_level` | (fail code) | dispatch: no granted level to revoke and no mapping for the area on the PACS; fulfillment → `Failed` |
| `door_command.expired` | (fail code) | gateway: a `MomentaryUnlock` not delivered before its `ExecuteBy`; connector command → `Failed` |
| `connector.not_found` | NotFound | CLI / registration service |
| `connector.already_exists` | Conflict | CLI `connector create` |
| `task.not_found` | NotFound | handlers (also when not visible) |
| `task.invalid_transition` | Conflict | StaffTask (also concurrent update) |
| `task.resolution_required` | Validation | StaffTask.Resolve / resolve endpoint (missing or not allowed action, note > 500) |
| `auth.forbidden` | Forbidden | generic policy failure |
| `door_command.reason_required` | Validation | DoorCommand.Request (reason 1-500) |
| `door_command.invalid_duration` | Validation | DoorCommand.Request (`MomentaryUnlock` 1-60 s and scope `Door`; others no duration) |
| `door_command.scope_not_found` | NotFound | request door command (also when outside the staff member's sites) |
| `door_command.no_connector_doors` | Conflict | no door in scope is handled by a `Connector`-mode instance |
| `door_command.unsupported_by_pacs` | Conflict | (M6) connector doors exist in scope, but none of their PACS instances supports the command kind (capability gap) |
| `alarm.not_found` | NotFound | handlers (also when not visible) |
| `alarm.comment_required` | Validation | Alarm.Acknowledge |
| `alarm.already_acknowledged` | Conflict | Alarm.Acknowledge |
| `reconciliation.instance_not_found` | NotFound | request reconciliation / list reports (unknown instance or outside the staff member's sites) |
| `reconciliation.report_not_found` | NotFound | handlers (also when not visible) |
| `reconciliation.connector_required` | Conflict | request on a `Manual`-mode instance |
| `reconciliation.export_unsupported` | Conflict | instance capabilities report `RecordExport: false` |
| `reconciliation.already_running` | Conflict | a report of the instance is `Requested`/`Exporting`/`Comparing` |
| `reconciliation.invalid_transition` | Conflict | ReconciliationReport |
| `reconciliation.timed_out` | (failure code) | scheduler: report not completed within `Reconciliation:TimeoutHours` |
| `tenant.not_found` | NotFound | (M10) platform routes |
| `tenant.invalid_email_domain` | Validation | (M10) `CreateTenantRequest` |
| `tenant.email_domain_taken` | Conflict | (M10) another tenant or Keycloak organization owns the domain |
| `tenant.invalid_status_transition` | Conflict | (M10) Tenant transitions |
| `tenant.provisioning_not_failed` | Conflict | (M10) `TenantProvisioning.Retry` |
| `tenant.admin_already_active` | Conflict | (M10) resend invite after the first admin signed in |
| `tenant.provisioning_failed` | (failure code) | (M10) stored on `TenantProvisioning.LastError` prefix, e.g. `tenant.provisioning_failed: Organization: ...` |
| `platform.operator_not_found` | Forbidden | (M10) `PlatformOperatorHandler` |
| `idp.invalid_config` | Validation | (M10) `SaveIdentityProviderRequest` (with `errors`) |
| `idp.url_not_allowed` | Validation | (M10) `IdpMetadataFetcher` (scheme, private address, redirect) |
| `idp.unreachable`, `idp.metadata_invalid` | (test check codes) | (M10) `IdpTestResultDto.Checks[].Code` |
| `idp.not_configured` | NotFound | (M10) delete/test-sign-in without IdP |
| `idp.provisioning_failed` | Failure (502) | (M10) Keycloak admin API refused or unreachable |
| `site.not_found`, `floor.not_found`, `area.not_found`, `door.not_found`, `reader.not_found`, `security_level.not_found` | NotFound | (M10) admin handlers |
| `site.not_empty`, `floor.not_empty`, `area.in_use` | Conflict | (M10) deletion rules §2.10 |
| `area.name_taken`, `area.code_taken`, `area.overlaps` | Conflict / Conflict / Validation | (M10) Area admin edits |
| `door.ref_taken` | Conflict | (M10) `PacsDoorRef` unique per PACS instance |
| `door.invalid_position`, `reader.invalid_position` | Validation | (M10) outside the floor plan |
| `floor.background_too_large` | Validation (413) | (M10) > 10 MiB |
| `floor.background_unsupported_type` | Validation | (M10) sniffed type not JPEG/PNG or undecodable |
| `floor.background_not_found` | NotFound | (M10) |
| `people_import.invalid_file` | Validation | (M10) not CSV/UTF-8, missing header columns `email,given_name,family_name` |
| `people_import.too_large` | Validation (413) | (M10) > 1 MiB or > 5000 rows |
| `role.assignment_not_found` | NotFound | (M10) |
| `role.assignment_exists` | Conflict | (M10) same person + role + site |
| `role.last_tenant_admin` | Conflict | (M10) removing the last `TenantAdmin` |
| `role.person_not_employee` | Validation | (M10) roles only for active Employees |
| `pacs.not_found` | NotFound | (M10) |
| `pacs.name_taken` | Conflict | (M10) |
| `pacs.in_use` | Conflict | (M10) deletion rules §2.10 |
| `pacs.invalid_site` | Validation | (M10) unknown site id |
| `mapping.area_not_on_instance_site` | Validation | (M10) mapping for an area outside the instance's sites |
| `connector.not_found` | NotFound | (M10) revoke without a non-revoked registration |
| `onboarding.step_not_skippable` | Conflict | (M10) |
| `onboarding.incomplete` | Conflict | (M10) complete with required steps open |
| `server.unexpected` | Failure | unhandled exception (API exception handler) |
