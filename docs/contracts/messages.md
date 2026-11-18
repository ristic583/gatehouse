# Messages contract

Every domain event, connector command and connector event. Spec §7.1. Decisions: ADR-005. Wire rules for connector traffic: `connector-protocol.md`.

## 0. Conventions

- **Domain events** are C# `sealed record`s implementing `IDomainEvent` in `Gatehouse.Domain.{Module}.Events`. They are raised by aggregates, written to `outbox_messages` by `DomainEventOutboxInterceptor`, and published to RabbitMQ by `OutboxRelayWorker` as the same record type (namespace `Gatehouse.Domain.*.Events`, MassTransit's default message URN; no MassTransit attributes in Domain). The outbox `type` column is the CLR type name, resolved by `OutboxMessageTypeRegistry` (names must be unique).
- Every domain event has these **base fields** (not repeated below): `EventId: Guid`, `OccurredAt: DateTimeOffset`. Domain events do **not** carry a tenant field (entities have no tenant column, ADR-001): `DomainEventOutboxInterceptor` stores the event in the tenant's own outbox, and `OutboxRelayWorker` publishes it with the mandatory header `gh-tenant-id`. Consumers read the tenant from `ITenantContext`, set by `TenantScopeConsumeFilter` from that header.
- **Connector commands and events** are records in `Gatehouse.Connector.Contracts` (namespace `Gatehouse.Connector.Contracts.Commands` / `.Events`), shared by gateway and agent. They travel as the `payload` of the envelope in `connector-protocol.md`; envelope fields (`commandId`, `idempotencyKey`, `partitionKey`, `sequence`, `priority`, ...) are not repeated.
- **Gateway → platform integration messages** (published by the gateway to RabbitMQ) are records in `Gatehouse.Application.Connectors.Messages`.
- Field types use C# names; JSON is camelCase. `?` = nullable.
- **Never** put PIN values, tokens, full card numbers or connection strings in a domain event or integration message.
- Milestone in brackets = when the message is first produced.

---

## 1. Domain events

### People
| Event | Fields |
|---|---|
| `PersonCreated` [M2] | `PersonId PersonId`, `PersonType Type`, `PersonSource Source`, `PersonId? ManagerId`, `PersonId? SponsorId` |
| `PersonUpdated` [M8] | `PersonId PersonId`, `string[] ChangedFields` |
| `EligibilityChanged` [M8] | `PersonId PersonId`, `DateTimeOffset Start`, `DateTimeOffset? End`, `DateTimeOffset? PreviousEnd` |
| `PersonTerminated` [M8] | `PersonId PersonId`, `DateTimeOffset EligibilityEnd` |
| `SponsorshipEnded` [M8] | `PersonId PersonId`, `PersonId SponsorId`, `PersonId? EndedById` |

### Access
| Event | Fields |
|---|---|
| `AccessRequested` [M2] | `EntitlementId EntitlementId`, `PersonId PersonId`, `AreaId AreaId`, `PersonId RequestedById`, `DateTimeOffset RequestedStart`, `DateTimeOffset RequestedEnd`, `DateTimeOffset EffectiveEnd`, `bool CappedToEligibility` |
| `ApprovalStepActivated` [M2] | `EntitlementId EntitlementId`, `ApprovalStepId StepId`, `int Order`, `ApproverKind ApproverKind`, `PersonId? AssignedApproverId` |
| `ApprovalGranted` [M2] | `EntitlementId EntitlementId`, `ApprovalStepId StepId`, `ApproverKind ApproverKind`, `PersonId DecidedById`, `string? Comment` |
| `ApprovalRejected` [M2] | `EntitlementId EntitlementId`, `ApprovalStepId StepId`, `PersonId DecidedById`, `string Comment` |
| `EntitlementApproved` [M2] | `EntitlementId EntitlementId`, `PersonId PersonId`, `AreaId AreaId`, `DateTimeOffset Start`, `DateTimeOffset EffectiveEnd` |
| `EntitlementRejected` [M2] | `EntitlementId EntitlementId`, `PersonId PersonId`, `AreaId AreaId` |
| `EntitlementCancelled` [M3] | `EntitlementId EntitlementId`, `PersonId CancelledById` |
| `EntitlementRevoked` [M3] | `EntitlementId EntitlementId`, `PersonId PersonId`, `AreaId AreaId`, `PersonId? RevokedById`, `string Reason`, `RevocationKind Kind` |
| `EntitlementExpired` [M8] | `EntitlementId EntitlementId`, `PersonId PersonId`, `AreaId AreaId` |
| `RenewalDue` [M8] | `PersonId PersonId`, `EntitlementId EntitlementId`, `DateTimeOffset DueAt` |

### Fulfillment
| Event | Fields |
|---|---|
| `FulfillmentDesired` [M2] | `FulfillmentId FulfillmentId`, `EntitlementId EntitlementId`, `PacsInstanceId PacsInstanceId`, `FulfillmentKind Kind`, `CommandPriority Priority`, `int Revision` |
| `FulfillmentDispatched` [M4] | `FulfillmentId FulfillmentId`, `CommandId CommandId`, `int Attempt` (raised by `MarkPending` with a non-null command id) |
| `FulfillmentConfirmed` [M3] | `FulfillmentId FulfillmentId`, `EntitlementId EntitlementId`, `FulfillmentKind Kind`, `ConfirmerKind ConfirmedByKind`, `string ConfirmedById`, `DateTimeOffset ConfirmedAt`, `string? PacsRef` |
| `FulfillmentFailed` [M3] | `FulfillmentId FulfillmentId`, `EntitlementId EntitlementId`, `FulfillmentKind Kind`, `string ErrorCode`, `string ErrorMessage`, `int Attempts` |
| `FulfillmentSuperseded` [M3] | `FulfillmentId FulfillmentId`, `EntitlementId EntitlementId`, `FulfillmentKind Kind` (raised when a revocation supersedes an unconfirmed grant; M4 marks its connector commands `Superseded`) |
| `FulfillmentDriftDetected` [M7] | `FulfillmentId FulfillmentId`, `EntitlementId EntitlementId`, `ReconciliationFindingId? FindingId` |
| `RevocationUnconfirmedAlert` [M3] | `EntitlementId EntitlementId`, `FulfillmentId FulfillmentId`, `DateTimeOffset RevokedAt`, `TimeSpan Outstanding` (raised by `Fulfillment.FlagRevocationUnconfirmed`, called by the Workers revocation monitor once per unresolved `RevocationUnconfirmed` task) |

### Credentials
| Event | Fields |
|---|---|
| `CardAssigned` [M8] | `CredentialId CredentialId`, `PersonId PersonId`, `string CardNumberMasked` |
| `CardActivationRequested` [M8] | `CredentialId CredentialId`, `PersonId PersonId`, `PersonId RequestedById` |
| `CardActivated` [M8] | `CredentialId CredentialId`, `PersonId PersonId` |
| `CardDeactivated` [M8] | `CredentialId CredentialId`, `PersonId PersonId`, `string Reason` |
| `CardRevoked` [M8] | `CredentialId CredentialId`, `PersonId PersonId`, `string Reason` |
| `PinSet` [M8] | `PersonId PersonId`, `PersonId SetById` (no value) |
| `PinRevealed` [M8] | `PersonId PersonId`, `PersonId RevealedById`, `StaffTaskId TaskId` (no value) |

### Live building
| Event | Fields |
|---|---|
| `DoorCommandRequested` [M5] | `DoorCommandId DoorCommandId`, `DoorCommandKind Kind` (`MomentaryUnlock`, `LockdownStart`, `LockdownClear`), `DoorCommandScope Scope` (`Door`, `Area`, `Floor`, `Site`), `Guid ScopeId`, `PersonId RequestedById`, `string Reason`, `int? DurationSeconds` |
| `AlarmRaised` [M5] | `AlarmId AlarmId`, `DoorId DoorId`, `AlarmKind Kind`, `Guid DoorEventId` |
| `AlarmAcknowledged` [M5] | `AlarmId AlarmId`, `PersonId AcknowledgedById`, `string Comment` |
| `LiveDriftAlertRaised` [M7] | `AlarmId AlarmId`, `PersonId? PersonId`, `AreaId AreaId`, `DoorId DoorId`, `Guid DoorEventId` (raised with `AlarmRaised` by `Alarm.RaiseUnauthorizedAccess`) |

### Reconciliation and tasks
| Event | Fields |
|---|---|
| `ReconciliationRequested` [M7] | `ReconciliationReportId ReportId`, `PacsInstanceId PacsInstanceId`, `PersonId? RequestedById` |
| `ReconciliationCompleted` [M7] | `ReconciliationReportId ReportId`, `int ComparedRecords`, `int FindingCount` |
| `ReconciliationFailed` [M7] | `ReconciliationReportId ReportId`, `string Code` |
| `ReconciliationFindingCreated` [M7] | `ReconciliationFindingId FindingId`, `ReconciliationReportId ReportId`, `FindingKind Kind`, `StaffTaskId TaskId` |
| `StaffTaskCreated` [M3] | `StaffTaskId TaskId`, `StaffTaskKind Kind`, `SiteId? SiteId` |
| `StaffTaskResolved` [M3] | `StaffTaskId TaskId`, `PersonId? ResolvedById` (null = resolved by the system), `string ResolutionAction` |

### Connectors
| Event | Fields |
|---|---|
| `ConnectorWentOffline` [M5] (needs the offline sweeper) | `PacsInstanceId PacsInstanceId`, `DateTimeOffset LastHeartbeatAt` |
| `ConnectorCameOnline` [M4] | `PacsInstanceId PacsInstanceId`, `DateTimeOffset? OfflineSince` (raised by `PacsInstance.RecordHeartbeat`) |

---

## 2. Connector commands (cloud → agent)

Payload records in `Gatehouse.Connector.Contracts.Commands`. "Adapter call" = the `IPacsAdapter` method the agent invokes (`adapter-interface.md`).

| Command | Payload fields | Partition | Priority | Adapter call |
|---|---|---|---|---|
| `UpsertCardholder` | `string PersonRef`, `string GivenName`, `string FamilyName`, `string? CardNumber`, `string? PhotoUrl` (signed, 10 min) | `person:{id}` | Normal | `UpsertCardholderAsync` |
| `GrantAccess` | `string PersonRef`, `string CardNumber`, `string VendorAccessLevelRef`, `DateTimeOffset ValidFrom`, `DateTimeOffset ValidTo` | `person:{id}` | Normal | `GrantAccessAsync` |
| `RevokeAccess` | `string PersonRef`, `string CardNumber`, `string VendorAccessLevelRef`, `RevocationKind Kind` | `person:{id}` | Normal; **High** if `Kind` is `Emergency` | `RevokeAccessAsync` |
| `ActivateCard` | `string PersonRef`, `string CardNumber` | `person:{id}` | Normal | `SetCardStateAsync(TargetState = Active)` |
| `SuspendCard` | `string PersonRef`, `string CardNumber`, `CardTargetState TargetState` (`Suspended`, `Revoked`), `string Reason` | `person:{id}` | Normal; High on termination | `SetCardStateAsync` |
| `SetPin` | `string PersonRef`, `string CardNumber`, `string EncryptedPin` (base64 RSA-OAEP-SHA256 ciphertext to the agent certificate's public key) | `person:{id}` | Normal | `SetPinAsync` (capability `pinWrite`) |
| `MomentaryUnlock` | `string[] PacsDoorRefs`, `int DurationSeconds` (1-60), `string Reason`, `DateTimeOffset? ExecuteBy` (request time + 60 s; gateway and agent drop it afterwards: GH-LIVE-01; missing → envelope `issuedAt` + 60 s, `MomentaryUnlock.DefaultDeadline`, PT-33) | `door:{doorId}` (one command per door) | High | `DoorCommandAsync` |
| `Lockdown` | `string[] PacsDoorRefs`, `LockdownAction Action` (`Start`, `Clear`), `string Reason` | `door:{doorId}` | High | `DoorCommandAsync` |
| `ExportRecords` | `Guid ExportId`, `RecordType[] RecordTypes` (`Cardholder`, `Card`, `AccessAssignment`), `string[]? VendorAccessLevelRefs` (null = all) | `export:{exportId}` | Normal | `ExportRecordsAsync` |

`personRef` is always the Gatehouse `PersonId` as a string; vendors store it as their external reference.

---

## 3. Connector events (agent → cloud, over HTTP)

Records in `Gatehouse.Connector.Contracts.Events`; HTTP shapes in `connector-protocol.md` §2.

| Event | Endpoint | Fields |
|---|---|---|
| `CommandAcked` | `POST /acks` | `Guid CommandId`, `string IdempotencyKey`, `AckStatus Status` (`Applied`, `Failed`, `Unsupported`), `string? PacsRef`, `DateTimeOffset? AppliedAt`, `AckError? Error` (`string Code`, `string Message`, `bool Retryable`) |
| `DoorEvent` | `POST /events` | `Guid EventId`, `DateTimeOffset OccurredAt`, `DoorEventType Type`, `string PacsDoorRef`, `string? PacsReaderRef`, `string? CardNumber`, `string? PersonRef`, `ReaderDirection? Direction` |
| `RecordExport` | `POST /exports` | `Guid ExportId`, `Guid CommandId`, `int ChunkIndex`, `bool IsLast`, `DateTimeOffset CapturedAt`, `PacsRecord[] Records` |
| `Heartbeat` | `POST /heartbeat` | `string AgentVersion`, `AdapterInfo Adapter` (`string Vendor`, `string Version`), `PacsCapabilities Capabilities`, `int QueueDepth`, `bool PacsReachable`, `DateTimeOffset AgentTime` |
| `CapabilitiesReported` | derived by gateway from `Heartbeat` when capabilities change | `PacsCapabilities Capabilities` |

## 4. Gateway → platform integration messages (RabbitMQ)

Records in `Gatehouse.Application.Connectors.Messages`; every one has `TenantId string`, `PacsInstanceId PacsInstanceId`, `ConnectorId ConnectorId`, `DateTimeOffset ReceivedAt`.

| Message | Extra fields | Consumer (Workers) |
|---|---|---|
| `ConnectorCommandAcked` | `CommandId CommandId`, `FulfillmentId? FulfillmentId`, `AckStatus Status`, `string? PacsRef`, `AckError? Error`, `bool IsFinal` | `FulfillmentAckConsumer`; `ReconciliationExportAckConsumer` [M7] (final non-`Applied` ack of an `ExportRecords` command → report `Failed`) |
| `DoorEventsReceived` | `NormalizedDoorEvent[] Events` (card number already hashed) | `DoorEventIngestConsumer` |
| `RecordExportReceived` [M7] | `Guid ExportId`, `int ChunkCount`, `int RecordCount`, `DateTimeOffset CapturedAt` (published once, after the last chunk; records are staged by the gateway in `reconciliation_export_records`, not carried in the message) | `ReconciliationExportConsumer` |
| `ConnectorHeartbeatReceived` [M5] | `string AgentVersion`, `int QueueDepth`, `bool PacsReachable` | `ConnectorHealthConsumer` [M5] |
| `ConnectorCapabilitiesReported` | `PacsCapabilities Capabilities` | none in M4 (gateway stores capabilities on `PacsInstance` directly; published only on change); `ConnectorHealthConsumer` [M5] |

M4: the gateway updates `PacsInstance` heartbeat fields directly and does not publish `ConnectorHeartbeatReceived`. M5: it still updates them directly and also publishes `ConnectorHeartbeatReceived` on every heartbeat.

`NormalizedDoorEvent` (M5): `Guid EventId`, `DateTimeOffset OccurredAt`, `DoorEventType Type`, `string PacsDoorRef`, `string? PacsReaderRef`, `string? CardNumberHash` (HMAC-SHA-256 lowercase hex, per-tenant key; PT-34), `string? PersonRef`, `ReaderDirection? Direction` (enums from `Gatehouse.Connector.Contracts.Events`). Gateway messages are written to the tenant outbox (`IOutboxWriter.Enqueue`) in the gateway's transaction and published by the relay like domain events.
