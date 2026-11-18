# Architecture decision records

| # | Decision | Status |
|---|---|---|
| [001](001-database-per-tenant.md) | Database-per-tenant with a catalog database | Accepted |
| [002](002-keycloak-organizations.md) | One Keycloak realm with Organizations, Next.js BFF | Accepted |
| [003](003-long-poll-agent-channel.md) | HTTPS long-poll for the connector agent channel | Accepted |
| [004](004-modular-monolith.md) | Modular monolith, process boundaries at gateway and agent | Accepted |
| [005](005-outbox-and-messaging.md) | Custom per-tenant outbox relay on MassTransit 8 transport; command table for connectors | Accepted |
| [006](006-door-event-storage.md) | Door events in native Postgres daily partitions | Accepted |
| [007](007-map-rendering-svg.md) | Plain SVG React components for floor maps | Accepted |
| [008](008-in-process-side-effects-before-m4.md) | In-process cross-module side effects until the event backbone exists (M3) | Accepted |
| [009](009-platform-operators-and-runtime-tenant-provisioning.md) | Platform operator realm and runtime tenant provisioning (Keycloak admin API) | Accepted |

New ADRs use the next free number, the same headings (Status, Date, Spec refs, Context, Decision, Consequences, Alternatives considered) and are added to this table. An accepted ADR is never edited to reverse it; write a new ADR that supersedes it.
