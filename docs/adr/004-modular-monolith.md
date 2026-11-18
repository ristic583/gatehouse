# ADR-004: Modular monolith first, with real process boundaries only at the gateway and the agent

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §1, §12, §13

## Context

The platform has many concerns (people, map, approvals, fulfillment, audit, reconciliation, live building). A microservice split would add network hops, distributed transactions and deployment overhead before the domain is understood. However, two boundaries are real from day one:

- The **connector gateway** is the only internet-facing endpoint for agents, has its own auth (mTLS) and its own scaling profile (many long-lived polls).
- The **agent** runs in a different network, owned by the customer.

## Decision

Build one **modular monolith codebase** deployed as a small number of hosts:

| Host project | Process | Contains |
|---|---|---|
| `Gatehouse.Api` | web | Minimal API endpoints, SignalR hubs, auth |
| `Gatehouse.Workers` | worker | Outbox relay, scheduled jobs (expiry, termination, reconciliation, partition maintenance), message consumers |
| `Gatehouse.ConnectorGateway` | web (separate port/hostname) | `/connector/v1/*` only (added in M4) |
| `Gatehouse.Cli` | console | `migrate`, `tenant create`, `seed`, `bootstrap` |
| `Gatehouse.Agent` | worker, customer side | Hosts exactly one `IPacsAdapter` (added in M4) |

All cloud hosts share `Gatehouse.Domain`, `Gatehouse.Application` and `Gatehouse.Infrastructure`. The agent shares **only** `Gatehouse.Connector.Contracts`.

**Modules** are feature folders inside `Gatehouse.Application` (and matching folders in Domain, Infrastructure and Api):

`Tenancy`, `Identity` (current user, roles), `Sites` (sites, floors, areas, doors, readers, security levels), `People` (persons, eligibility, sponsorship), `Access` (entitlements, approval steps, request policy, map state), `Fulfillment`, `Credentials`, `Audit`, `Connectors`, `LiveBuilding`, `Reconciliation`, `Tasks` (staff tasks), `Theming`.

Module rules:
- A module may read another module's entities through `ITenantDb` but **writes only its own aggregates**. Cross-module side effects go through domain events (in-process via the outbox → RabbitMQ, ADR-005).
- No module calls another module's handler directly.
- Hosts contain no business logic: endpoints validate input shape, call one handler, map the `Result` to HTTP.
- Use plain handler classes (`XHandler.HandleAsync(XCommand, CancellationToken)`) registered explicitly in a per-module `XModule.cs`. No MediatR (now commercially licensed) and no runtime assembly scanning.
- Additions to the spec's §13 layout: `src/Gatehouse.ServiceDefaults` (Aspire service defaults) and `src/Gatehouse.Cli` (the `gatehouse-cli` tool).

## Consequences

**Positive**
- One transaction per use case; no distributed sagas for core flows.
- Refactoring across modules is a compiler-checked change.
- The gateway can scale and be exposed independently; agents only depend on a small contract package.

**Negative**
- Module boundaries are enforced by convention and review, not by the runtime. Mitigation: the folder rules above and per-module registration make violations visible in diffs.
- Api and Workers deploy together on every change.

## Alternatives considered

- **Microservices per module:** rejected as premature; it would mostly demonstrate infrastructure, not the domain.
- **Single host for everything including the gateway:** rejected; the gateway's mTLS listener and public exposure should not share a process with the user-facing API.
