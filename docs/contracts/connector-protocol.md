# Connector protocol contract (v1)

How a connector agent talks to the Connector Gateway. Spec: §1 (golden rule), §7.2, §7.3. Decisions: ADR-003 (long-poll), ADR-005 (command table). Message payloads: `messages.md`. Implemented from M4.

**Golden rule:** the agent only makes outbound HTTPS requests to the gateway. The gateway never connects to the agent.

## 1. Transport and auth

- Base URL: `https://{gateway-host}/connector/v1`. Local dev: the `gateway` Aspire resource (HTTPS endpoint).
- **mTLS:** every endpoint except `/enroll` requires a client certificate issued by the Gatehouse connector CA.
  - Certificate subject `CN=gatehouse-connector-{connectorId}`; SAN URI `urn:gatehouse:tenant:{tenantId}:pacs:{pacsInstanceId}:connector:{connectorId}`.
  - Key: RSA 3072 (needed so the platform can encrypt `SetPin` payloads to the agent, see `messages.md`). Validity 90 days; the agent re-enrolls through `/renew` (M9 stretch) or a new enrollment token.
  - The gateway authenticates the cert (`ConnectorCertificateAuthenticationHandler`), then loads the `ConnectorRegistration` by thumbprint from the catalog and checks: registration `Active`, thumbprint matches, SAN tenant and pacs instance equal the registration's. Any mismatch → `403 connector.scope_mismatch`.
  - The resolved `(TenantId, PacsInstanceId, ConnectorId)` becomes the request's connector context. **Every** read and write is restricted to that tenant DB and that PACS instance; ids in request bodies that point elsewhere are rejected per item with `connector.scope_mismatch`.
- **Fallback (documented, not built by default):** OAuth 2.0 client credentials from Keycloak with a client per connector and the same tenant/instance claims.
- Required headers on every request:
  - `X-Gatehouse-Contract-Version: 1` (else `400 connector.contract_version_unsupported`)
  - `X-Gatehouse-Agent-Version: {semver}`
  - `traceparent` (W3C) is propagated when present.
- JSON: UTF-8, camelCase, enums as strings, instants ISO-8601 UTC (`2026-11-09T08:42:13.512Z`).
- `POST /events` and `POST /exports` bodies may be gzip-compressed (`Content-Encoding: gzip`); max decompressed body 5 MiB (`413 connector.payload_too_large`).
- Rate limits per connector: 10 req/s sustained, burst 50 → `429 connector.rate_limited` with `Retry-After` seconds. Requests without a valid client certificate (incl. `/enroll`) share one bucket per remote IP: burst 20, then 10 per minute (PT-30).

## 2. Endpoints

| Method + path | Auth | Purpose |
|---|---|---|
| `POST /connector/v1/enroll` | one-time token | exchange enrollment token + CSR for a client certificate |
| `POST /connector/v1/poll` | mTLS | long-poll for commands (≤ 30 s) |
| `POST /connector/v1/acks` | mTLS | acknowledge command results |
| `POST /connector/v1/events` | mTLS | push batched door events |
| `POST /connector/v1/heartbeat` | mTLS | liveness, version, capabilities, queue depth |
| `POST /connector/v1/exports` | mTLS | upload a chunk of a record export |

### 2.1 Enroll

Request:
```json
{
  "enrollmentToken": "ghe_…",
  "csrPem": "-----BEGIN CERTIFICATE REQUEST-----\n...",
  "agentVersion": "1.0.0"
}
```
Response `200`:
```json
{
  "connectorId": "01933b8e-6f0a-7c4e-9a52-0f3c1d2e4b51",
  "tenantId": "nordlys",
  "pacsInstanceId": "01933b8e-70aa-7d10-8c11-55e3a0b9e001",
  "certificatePem": "-----BEGIN CERTIFICATE-----\n...",
  "caChainPem": "-----BEGIN CERTIFICATE-----\n...",
  "notAfter": "2027-02-07T10:00:00Z"
}
```
Rules: the token is single-use (hash compared in constant time, then cleared; the registration row is locked for the enrollment, so parallel calls with one token get one certificate and `401 connector.enrollment_invalid` otherwise, PT-31), expires 24 h after creation; CSR must be RSA 3072 with a valid signature (proof of possession, PT-32) and its subject is ignored (the gateway sets subject and SAN). Errors: `401 connector.enrollment_invalid`, `401 connector.enrollment_expired`, `400 connector.csr_invalid`. The enrollment token is never logged.

### 2.2 Poll

Request:
```json
{ "maxBatch": 50, "waitSeconds": 25 }
```
`maxBatch` 1-100 (default 50), `waitSeconds` 0-30 (default 25). Response `200` (empty `commands` array after the wait times out):
```json
{
  "serverTime": "2026-11-09T08:42:13.512Z",
  "commands": [
    {
      "commandId": "01933b9a-1c2d-7e3f-8a4b-5c6d7e8f9a0b",
      "idempotencyKey": "GrantAccess:01933b99-aaaa-7bbb-8ccc-dddddddddddd:1",
      "type": "GrantAccess",
      "schemaVersion": 1,
      "tenantId": "nordlys",
      "pacsInstanceId": "01933b8e-70aa-7d10-8c11-55e3a0b9e001",
      "partitionKey": "person:01933b90-1111-7222-8333-444444444444",
      "sequence": 12,
      "priority": "Normal",
      "issuedAt": "2026-11-09T08:42:12.900Z",
      "leaseUntil": "2026-11-09T08:44:13.512Z",
      "payload": {
        "personRef": "01933b90-1111-7222-8333-444444444444",
        "cardNumber": "100200300",
        "vendorAccessLevelRef": "AL-CPH-SERVER",
        "validFrom": "2026-11-09T00:00:00Z",
        "validTo": "2026-11-23T00:00:00Z"
      }
    }
  ]
}
```
Errors: `409 connector.poll_in_progress` (another poll is open for this PACS instance).

### 2.3 Acks

Request (1-100 items):
```json
{
  "acks": [
    {
      "commandId": "01933b9a-1c2d-7e3f-8a4b-5c6d7e8f9a0b",
      "idempotencyKey": "GrantAccess:01933b99-aaaa-7bbb-8ccc-dddddddddddd:1",
      "status": "Applied",
      "pacsRef": "inbox-row-88412",
      "appliedAt": "2026-11-09T08:42:15.020Z",
      "error": null
    },
    {
      "commandId": "01933b9a-2222-7e3f-8a4b-5c6d7e8f9a0c",
      "idempotencyKey": "SetPin:01933b99-eeee-7bbb-8ccc-dddddddddddd:1",
      "status": "Unsupported",
      "pacsRef": null,
      "appliedAt": null,
      "error": { "code": "adapter.capability_missing", "message": "PIN write not supported by LegacyPACS", "retryable": false }
    }
  ]
}
```
`status`: `Applied` | `Failed` | `Unsupported`. `error` is required unless `Applied`. Response `200`:
```json
{ "results": [ { "commandId": "01933b9a-1c2d-...", "accepted": true, "reason": null },
               { "commandId": "01933b9a-2222-...", "accepted": true, "reason": null } ] }
```
Per-item `accepted: false` reasons: `connector.unknown_command`, `connector.scope_mismatch`, `connector.idempotency_key_mismatch`, `connector.already_final` (a late duplicate ack is accepted only if it matches the stored final status; otherwise `already_final`).

### 2.4 Events

Request (1-500 events, gzip recommended):
```json
{
  "events": [
    {
      "eventId": "01933b9b-0000-7aaa-8bbb-cccccccccccc",
      "occurredAt": "2026-11-09T08:42:13.000Z",
      "type": "AccessGranted",
      "pacsDoorRef": "D-2.04",
      "pacsReaderRef": "R-2.04",
      "cardNumber": "100200300",
      "personRef": "01933b90-1111-7222-8333-444444444444",
      "direction": "In"
    }
  ]
}
```
Response `202` `{ "accepted": 1, "duplicates": 0 }`. Dedupe by `eventId` (Redis 10 min + DB primary key). The gateway replaces `cardNumber` by a keyed hash (HMAC-SHA-256, per-tenant key derived from the server master key, `ICardNumberHasher`; PT-34) before publishing; raw card numbers never leave the gateway process. The wire format is unchanged: the agent still sends the raw number over mTLS.

### 2.5 Heartbeat

Request (every 15 s, and immediately on start):
```json
{
  "agentVersion": "1.0.0",
  "adapter": { "vendor": "LegacyPacs", "version": "1.0.0" },
  "capabilities": {
    "nativeValidity": true, "pinWrite": false, "photoWrite": false,
    "pushEvents": false, "recordExport": true,
    "doorCommands": ["MomentaryUnlock", "Lockdown"], "maxBatchSize": 50
  },
  "queueDepth": 3,
  "pacsReachable": true,
  "agentTime": "2026-11-09T08:42:13.100Z",
  "refusedRemovals": [
    { "revocationRef": "0193...:AL-SERVER", "code": "cloudpacs.level_locked", "firstRefusedAt": "2026-11-09T08:00:00Z", "attempts": 3 }
  ]
}
```
Response `200`:
```json
{ "serverTime": "2026-11-09T08:42:13.140Z", "pollWaitSecondsHint": 25, "desiredLogLevel": null }
```
`refusedRemovals` (M11-15, GH-CONN-03, optional; `null`/absent = agent does not report): every local scheduled revocation (`adapter-interface.md` §7) the PACS refused with a non-retryable error and that is still pending; `revocationRef` = `{personRef}:{vendorAccessLevelRef}`, `attempts` counts refusals. The agent repeats an entry in every heartbeat until the removal is applied (or a platform revoke cancels it). The gateway keeps one open `ScheduledRemovalRefused` staff task per (connector, `revocationRef`) and resolves it (`AutoResolved`) when a heartbeat that reports `refusedRemovals` no longer contains it.\n\nThe gateway stores `LastHeartbeatAt` (server clock) and publishes `ConnectorHeartbeatReceived`; if `capabilities` differs from the stored value it also publishes `ConnectorCapabilitiesReported`.

### 2.6 Exports

Sent in response to an `ExportRecords` command. Request (≤ 1000 records per chunk):
```json
{
  "exportId": "01933b9c-0000-7aaa-8bbb-cccccccccccc",
  "commandId": "01933b9c-1111-7aaa-8bbb-cccccccccccc",
  "chunkIndex": 0,
  "isLast": false,
  "capturedAt": "2026-11-09T09:00:00.000Z",
  "records": [
    {
      "recordType": "AccessAssignment",
      "pacsRef": "assign-5512",
      "personRef": "01933b90-1111-7222-8333-444444444444",
      "cardNumber": "100200300",
      "vendorAccessLevelRef": "AL-CPH-SERVER",
      "validFrom": "2026-11-09T00:00:00Z",
      "validTo": "2026-11-23T00:00:00Z",
      "isActive": true
    }
  ]
}
```
Response `202`. Chunks must arrive with `chunkIndex` 0..n without gaps; a duplicate chunk index is ignored. The export command is acked (`/acks`) only after the last chunk is accepted.

Gateway rules (M7): `exportId` is the `ReconciliationReport` id; a report that does not exist or belongs to another instance → `404 connector.export_not_found`; a `Failed`/`Completed` report → `409 connector.export_closed` (a duplicate chunk of a `Comparing`/`Completed` report is still `202`); `chunkIndex` beyond the next expected one → `409 connector.export_chunk_out_of_order` (the agent restarts from chunk 0; earlier chunks are ignored as duplicates); more than 1000 records → `413 connector.payload_too_large`. Accepted records are staged in `reconciliation_export_records` in the gateway transaction; after the last chunk the gateway publishes `RecordExportReceived`.

## 3. Delivery semantics (gateway side)

1. **Deliverable command** = `Status == Queued`, `NextAttemptAt <= now`, and **no other command in the same `(PacsInstanceId, PartitionKey)` is `Delivered`** (at most one in flight per partition) and no `Queued` command in that partition has a lower `Sequence`.
2. **Order of selection:** `Priority DESC` (High first), then `Sequence ASC` within a partition, then `CreatedAt ASC` across partitions. Up to `maxBatch`.
3. Delivering sets `Status = Delivered`, `DeliveredAt = now`, `LeaseUntil = now + 120 s`, `Attempts += 1`. The fulfillment (if any) moves `Desired → Pending` (`MarkPending(commandId)`) in the same transaction as the poll; this is the one place the gateway writes a Fulfillment directly.
4. **Lease expiry:** a `Delivered` command whose `LeaseUntil` passed returns to `Queued` (same `commandId` and `idempotencyKey`), checked at the start of each poll for that instance. If `Attempts >= 5` it becomes `Failed` with `connector.lease_expired` instead (and rule 8 applies). A late ack for a command that went back to `Queued` with `Attempts > 0` is still accepted.
5. **Ack Applied** → `Acked`; publishes `ConnectorCommandAcked`; fulfillment → `Confirmed` (by `Agent`, `ConfirmedAt` = server clock).
6. **Ack Failed, `retryable: true`** → `Queued` with `NextAttemptAt = now + min(5 s × 2^(Attempts-1), 5 min)`. When `Attempts` reaches **5** → `Failed`.
7. **Ack Failed, `retryable: false`, or `Unsupported`** → `Failed` immediately.
8. **Failed** → publishes `ConnectorCommandAcked` with the failure; the fulfillment → `Failed`, a `StaffTask(FulfillmentFailed)` is created and security staff + tenant admin are alerted. Nothing silently disappears.
9. **Supersession:** when a `RevokeAccess` is inserted for a `Revoke` fulfillment of entitlement E, every `Queued` command of the same instance whose `FulfillmentId` is a `Grant` fulfillment of E becomes `Superseded` in the same transaction (commands of other entitlements are never touched). `FulfillmentSuperseded` also supersedes the `Queued` commands of that fulfillment (idempotent). A `High` priority command ignores the busy-partition and lower-sequence parts of rule 1 (the only exception).
10. **Offline agent:** commands simply stay `Queued`. The map shows ⚪ Unknown for areas mapped to an offline instance (`domain-model.md` §3).
11. **Final ack states:** a duplicate ack for an `Acked`/`Failed` command with the same status is accepted (no change); a different status → `connector.already_final`.
12. Priority is stored as a string: order by `Priority == High ? 0 : 1`, never by the string. One command per partition per batch.

## 4. Idempotency (agent side)

- `idempotencyKey = "{commandType}:{sourceId}:{revision}"` where `sourceId` is the fulfillment id (or the door-command / export id) and `revision` is `Fulfillment.Revision` (1 for non-fulfillment commands). Automatic redelivery reuses the key; a staff retry increments the revision and therefore the key.
- The agent keeps a SQLite table `processed_commands (idempotency_key PK, command_id, status, pacs_ref, error_json, processed_at)`.
- On receiving a command: if its key exists → re-send the **stored** ack without calling the adapter. Otherwise call the adapter; store the result **only** if `Applied`, `Unsupported` or non-retryable `Failed`; then ack. Retryable failures are not stored, so a redelivery re-applies.
- Commands of one batch are processed sequentially in the order received.
- **Fence after a High priority revoke** (GH-CONN-02): a `High` priority `RevokeAccess` may overtake a `GrantAccess` of the same partition that is already delivered (§3 rule 9). The agent records the revoke's `sequence` per (`personRef`, `vendorAccessLevelRef`) and acks any not yet started `GrantAccess` for that pair with a lower `sequence` as `Failed` `agent.grant_fenced` (not retryable, adapter not called); the gateway keeps the grant's fulfillment `Superseded`. No new field: the existing command `sequence` is used (`adapter-interface.md` §7).

## 5. Error responses

All errors use `application/problem+json`:
```json
{
  "type": "https://gatehouse.dev/problems/connector.scope_mismatch",
  "title": "Connector scope mismatch",
  "status": 403,
  "code": "connector.scope_mismatch",
  "detail": "Certificate is bound to tenant 'nordlys' and PACS instance 01933b8e-70aa-..., request targeted another instance.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

| Code | HTTP | Meaning |
|---|---|---|
| `connector.unauthenticated` | 401 | no / invalid client certificate |
| `connector.enrollment_invalid` | 401 | unknown or used enrollment token |
| `connector.enrollment_expired` | 401 | token older than 24 h |
| `connector.csr_invalid` | 400 | CSR unparsable or not RSA 3072 |
| `connector.revoked` | 403 | registration revoked |
| `connector.tenant_inactive` | 403 | (M10) the registration's tenant is not `Active` (suspended, provisioning); checked on every request incl. `/enroll` |
| `connector.scope_mismatch` | 403 | cert tenant/instance ≠ registration or ≠ target |
| `connector.contract_version_unsupported` | 400 | missing/unsupported `X-Gatehouse-Contract-Version` |
| `connector.validation_failed` | 400 | body invalid; `errors` map included |
| `connector.poll_in_progress` | 409 | concurrent poll for the same instance |
| `connector.payload_too_large` | 413 | body > 5 MiB decompressed or too many items |
| `connector.rate_limited` | 429 | with `Retry-After` |
| `connector.unknown_command` | per-item | ack for a command id not in this instance |
| `connector.idempotency_key_mismatch` | per-item | ack key ≠ stored key |
| `connector.already_final` | per-item | conflicting ack for a final command |
| `connector.lease_expired` | (command failure code) | lease expired 5 times; command `Failed` |
| `connector.export_not_found` | 404 | (M7) unknown export id for this instance |
| `connector.export_closed` | 409 | (M7) export's report already `Failed`/`Completed` |
| `connector.export_chunk_out_of_order` | 409 | (M7) chunk index skips ahead |

Agent client behaviour: `401`/`403` → stop polling, log error, retry enrollment check every 5 min; `429`/`5xx`/network errors → exponential backoff 1 s → 60 s with jitter; never drop unsent acks or events (buffered in SQLite).
