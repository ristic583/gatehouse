# ADR-005: Custom per-tenant outbox relay on top of MassTransit transport; a command table for connectors

- **Status:** Accepted
- **Date:** 2026-11-09 (amended 2026-11-16, see "Amendment")
- **Spec refs:** §7.1, §7.2, §16 (fulfillment and events acceptance tests)

## Context

Every domain change that has side effects (approval → fulfillment, revocation → alert) must publish messages **exactly when** the change commits. MassTransit ships an EF Core transactional outbox, but its delivery service is bound to one `DbContext` with one connection. With **database-per-tenant** (ADR-001) the outbox tables live in N databases, and the built-in delivery service has no tenant context to pick a connection.

Separately, connector commands need **per-partition ordering**, **priority lanes** (emergency revoke, lockdown), **leases and redelivery** for long-polling agents, and must survive agent outages for hours. A RabbitMQ queue is a poor fit for "deliver the next command of partition X only after the previous one is acked, to whichever gateway replica the agent polls".

Licensing: MassTransit v9 is commercially licensed; v8 remains Apache-2.0.

## Decision

1. **Transport:** RabbitMQ via **MassTransit 8.x** (pin the latest 8.x). We use MassTransit for topology, publishing, consumers, retry policies and error (`_error`) queues. We do **not** use MassTransit's EF outbox/inbox.

2. **Domain/integration events - custom outbox per tenant DB:**
   - Aggregates raise domain events (`IDomainEvent`). `DomainEventOutboxInterceptor` (an EF `SaveChangesInterceptor` on `TenantDbContext`) serializes them into `outbox_messages` in the **same transaction** as the state change.
   - `OutboxRelayWorker` (in `Gatehouse.Workers`) loops over active tenants every 1 s, claims up to 100 rows with `SELECT ... FOR UPDATE SKIP LOCKED` ordered by `priority DESC, occurred_at`, publishes each with MassTransit (`MessageId` = outbox row id, header `gh-tenant-id`), then sets `dispatched_at`. Failures increment `attempts` and record `last_error`; rows are retried with exponential backoff (1 s → 5 min cap) and are never deleted until dispatched (a cleanup job deletes dispatched rows older than 7 days).
   - Consumers are idempotent via `processed_messages (message_id, consumer)` in the tenant DB, inserted in the same transaction as the consumer's own changes. A `TenantScopeConsumeFilter` reads `gh-tenant-id`, rejects messages without it (moved to `_error`), and opens the tenant DI scope.
   - Retry policy on every consumer endpoint: `UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)))`. After that MassTransit moves the message to the endpoint's `_error` queue = dead letter, visible and replayable in the Message Inspector (M9; in M4 dead letters are inspected in the RabbitMQ management UI).
   - (M4) Consumers live in `src/Gatehouse.Infrastructure/Messaging/Consumers/`, are thin (call one Application handler) and are registered explicitly by `AddGatehouseConsumers(IBusRegistrationConfigurator)`; `Gatehouse.Workers` hosts the bus. `OutboxMessageTypeRegistry` maps the outbox `type` (CLR type name) to the type of every `IDomainEvent` and every record in `Gatehouse.Application.Connectors.Messages`, and fails at startup on duplicate names.

3. **Connector commands - command table, not a queue:**
   - When a use case needs a PACS change it inserts a `ConnectorCommand` row (`connector_commands` table in the tenant DB) **in the same transaction** as the domain change. The row has a per-partition `sequence` taken from `connector_partition_sequences` (row-locked increment in the same transaction).
   - The gateway reads `connector_commands` directly (it shares Infrastructure) and implements ordering, priority, leases, retries and supersession as specified in `docs/contracts/connector-protocol.md`.
   - Gateway → platform traffic (acks, door events, exports, heartbeats) is published to RabbitMQ as integration messages (`ConnectorCommandAcked`, `DoorEventsReceived`, ...) and consumed by Workers.

4. **Tenant isolation in messaging:** exchanges and queues are **shared** (named by message type / consumer, MassTransit defaults with the `gatehouse` prefix). Isolation is enforced by the mandatory `gh-tenant-id` header on every message (integration messages from the gateway also carry a `TenantId` field) plus the consume filter. This deliberately replaces the spec's `tenant.{id}.*` queue naming: per-tenant queues would multiply queues by tenants × consumers and require runtime topology changes on tenant creation. Redis keys and SignalR groups keep the `tenant:{tenantId}:` prefix.

## Consequences

**Positive**
- True transactional guarantee per tenant DB without fighting the framework.
- Connector ordering and priority are simple SQL with clear tests.
- No dependency on commercially licensed MassTransit features.

**Negative**
- We own ~200 lines of relay code and its tests.
- Polling N tenant DBs every second costs N small queries/s. Acceptable for tens of tenants; Postgres `LISTEN/NOTIFY` can replace polling later.
- Shared queues mean a noisy tenant can delay others; acceptable at this scale, mitigated by per-tenant fairness in the relay (max 100 rows per tenant per loop).

## Alternatives considered

- **MassTransit EF outbox with one DbContext per tenant registered dynamically:** not supported cleanly; would require one bus outbox delivery service per tenant created at runtime.
- **Commands via RabbitMQ to the gateway, gateway buffers in memory/Redis:** loses commands on gateway restarts unless persisted again; ordering across gateway replicas becomes hard.
- **MassTransit v9:** commercial licence; not appropriate for an open portfolio project.

## Amendment (2026-11-16, PR #28)

Refines how the relay finds work; the outbox table, claim query, publish and retry rules above are unchanged.

- The relay is driven by a pending signal in Redis: after a commit that wrote outbox rows, `OutboxSignalInterceptor` sets
  `tenant:{id}:outbox:pending` (`IOutboxSignal` / `RedisOutboxSignal`); the relay takes the keys with `GETDEL` before reading the
  outbox, so no committed row is missed.
- `OutboxRelayScheduler` visits per tick (`Outbox:RelayIntervalMs`, 250 ms) only signalled tenants, tenants with rows due again
  (full batch or publish backoff), failed tenants after 1 s, and every tenant once per safety sweep (`Outbox:SweepIntervalMs`,
  default 10 s) in case a signal is lost.
- Without Redis, or on a Redis error, the relay falls back to polling every tenant every tick (`PollingOutboxSignal`).
- Visited tenants still run through `ITenantLoop` (bounded concurrency, pool released after each tenant, PR #27).
- Effect: with 20 idle tenants 83.7 → 0 connects/s (one sweep per 10 s in steady state); a new message for an idle tenant is relayed in
  338 ms. This replaces the "Postgres `LISTEN/NOTIFY` can replace polling later" note under Consequences.
