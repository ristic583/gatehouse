# ADR-006: Door events in native PostgreSQL range partitions (not TimescaleDB)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §4 (DoorEvent), §6.1 (time scrubber), §6.3, §10 (retention)

## Context

Door events are an append-only time series: badge swipes, opens, closes, alarms. The demo needs the last 24 h for the time scrubber, live drift detection needs fast lookups by person and area, and GDPR retention needs cheap deletion of old data per tenant. Volume per tenant in the simulator is roughly 10-50k events/day.

## Decision

Store door events in a **natively range-partitioned PostgreSQL table** in each tenant DB:

- Table `door_events` partitioned `BY RANGE (occurred_at)`, **one partition per UTC day**, named `door_events_yyyymmdd`.
- Primary key `(occurred_at, id)` where `id` is a `uuid` (the agent-generated `eventId`, used for deduplication). Unique dedupe is enforced per partition by that key; the gateway also deduplicates batches in Redis for 10 minutes (`tenant:{tenantId}:door-event:{eventId}`).
- Indexes per partition (declared on the parent): `(door_id, occurred_at DESC)`, `(person_id, occurred_at DESC)`.
- Partitions are created by `DoorEventPartitionMaintenanceJob` (Workers, hourly) for **today + 7 days ahead**, and partitions older than the tenant's retention (default 90 days) are **dropped** (`DROP TABLE`, not `DELETE`). A `door_events_default` partition catches anything outside the range and raises a warning log.
- EF Core maps `DoorEvent` normally; the partitioned DDL is written as raw SQL in the migration that creates the table (EF does not generate partitioned tables).
- Current door state is **not** derived from the table at read time; it is kept in Redis (`tenant:{tenantId}:door:{doorId}:state`) and rebuilt from the last 24 h of events on cache miss.

## Consequences

**Positive**
- No extension needed: works with the stock `postgres` image used by Aspire and Testcontainers, and on any managed Postgres.
- Retention is O(1) per day (drop partition).
- Queries for "last 24 h" prune to 1-2 partitions.

**Negative**
- We maintain partition creation ourselves (a small job + test).
- No continuous aggregates; occupancy counters are kept in Redis instead of computed from history.

## Alternatives considered

- **TimescaleDB hypertables:** automatic chunking, compression and continuous aggregates, but requires a non-stock image, some features are under the Timescale License (TSL), and managed Postgres offerings often lack it. Overkill at this volume.
- **Unpartitioned table + `DELETE` for retention:** simple but retention deletes cause bloat and long vacuums.
- **Separate event store (e.g. EventStoreDB, Kafka):** another moving part with no requirement that justifies it.
