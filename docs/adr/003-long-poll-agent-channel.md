# ADR-003: HTTPS long-poll (not WebSocket) for the connector agent channel

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §1 (golden rule), §7.2, §7.3

## Context

Connector agents run inside customer networks and may only open **outbound HTTPS (443)** connections. They must receive commands with low latency (the demo shows a map turning green within seconds), push door events, acknowledgements, exports and heartbeats, and keep working through corporate proxies that inspect TLS, cut idle connections and often block or break WebSocket upgrades.

## Decision

Use **plain HTTPS request/response with long-polling** for the command channel, and ordinary POSTs for everything the agent sends. The full contract is in `docs/contracts/connector-protocol.md`.

- `POST /connector/v1/poll` waits up to `waitSeconds` (max 30, default 25) and returns as soon as at least one command is deliverable. The gateway wakes waiting polls with a Redis pub/sub signal on channel `tenant:{tenantId}:pacs:{pacsInstanceId}:commands`, and also re-checks the command table every 2 s as a fallback.
- Acks, events, heartbeats and exports are separate POSTs; the agent can send them while a poll is outstanding (two HTTP connections).
- One agent instance = one PACS instance = one poller. The gateway allows at most one outstanding poll per PACS instance; a second concurrent poll returns `409 connector.poll_in_progress`.
- Authentication is mutual TLS with a per-instance client certificate (see the protocol doc). The same transport works for the OAuth client-credentials fallback.

## Consequences

**Positive**
- Works through any proxy that allows HTTPS; no upgrade headers, no sticky long-lived sockets.
- Stateless gateway instances: any gateway replica can serve any poll, state lives in Postgres and Redis.
- Easy to test with plain `HttpClient` and to inspect with standard tooling.
- Natural back-pressure: the agent only asks for more when it has capacity (`maxBatch`).

**Negative**
- Up to one extra round-trip of latency per batch versus a push socket (acceptable: < 2 s end-to-end target is dominated by PACS processing).
- Idle tenants still generate one request per ~25 s per agent. Negligible.
- No server-initiated messages outside a poll; e.g. "change log level" is returned in the heartbeat response instead.

## Alternatives considered

- **WebSocket / SignalR client in the agent:** lower latency and bidirectional, but fragile through TLS-inspecting proxies and harder to load-balance. Could be added later as an optional transport behind the same `IConnectorTransport` abstraction in the agent.
- **gRPC streaming:** requires HTTP/2 end to end; many corporate proxies downgrade to HTTP/1.1. Rejected.
- **Server-Sent Events:** one-directional and often buffered by proxies. Rejected.
