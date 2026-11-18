# 0. Design reasoning: from the problem to the architecture

Why Gatehouse is built the way it is. Every decision below starts from a fact about the real problem (a *force*) and
ends in something you can find in the code. Sources: `docs/SPEC.md` §0–3, §7, §9, §15 and ADR-001…008; read those for
the full detail. This chapter is the "why" in one place.

## 0.1 The starting point: what the problem really is

A large company already owns physical access control systems (PACS): card readers, door controllers and a vendor
server per building, often from different vendors. What hurts is not opening doors, it is **governance**:

- Who should be allowed into which room, and who approved it?
- Was the approved change really applied in the PACS, or is it stuck?
- When someone leaves, is every card and every access level really removed, in every building?
- Does the PACS still match what was approved (people added by hand, access never removed)?

So the first and most important decision is the positioning:

> **Gatehouse is a governance layer, not an access control system.** Door controllers keep making the open/deny decision
> locally. Gatehouse decides *who should have what*, gets it approved, pushes it to the PACS and **tracks whether it was
> really applied** (SPEC §0).

Everything else follows from that sentence plus four facts about the environment:

| # | Force (fact of the real world) | Leads to |
|---|---|---|
| F1 | Doors must work even when Gatehouse, the internet or the cloud is down. | Gatehouse is never in the door decision path. |
| F2 | PACS servers sit inside customer networks; nobody opens firewall ports into them. | Agent in the customer network, **outbound HTTPS only**. |
| F3 | Every vendor integrates differently (SQL tables, REST, webhooks) and some cannot do everything. | One adapter contract, one adapter per vendor, capabilities. |
| F4 | Many customer companies, each with sensitive personal data and its own identity provider. | Database per tenant, SSO broker, tenant from the token only. |
| F5 | Access decisions are security decisions that auditors check. | Approval chains, no self-approval, append-only hash-chained audit. |

## 0.2 F1: never in the door path → "desired vs confirmed"

Because the controller decides at the door, Gatehouse can only *ask* the PACS to change. The ask can fail, be delayed
for hours (agent offline), or be undone by someone at the PACS console. So Gatehouse cannot treat "approved" as
"has access". It keeps two states:

- **Desired**: what should be true (`entitlements` approved → `fulfillments` `Desired`/`Pending`).
- **Confirmed**: what the PACS acknowledged (`fulfillments` `Confirmed` after the agent's ack, or staff confirming by hand).

Consequences you can see:
- The map turns **green only after confirmation**; between approval and ack it shows *Pending · awaiting fulfillment*
  (`AreaAccessStateCalculator`, domain-model §3). Never claim more than you know.
- If the connector is offline the map says **Unknown**, not a guess.
- A failed fulfillment opens a **staff task**; a revocation that is not confirmed in time opens another one.
- **Reconciliation** (M7) exports the PACS state and compares it with *confirmed* state: missing, extra, mismatch,
  expired-still-present. "In sync" is reported only for what was actually compared.
- **Live drift**: an `AccessGranted` door event for someone with no confirmed entitlement raises an alert at once.
- `Manual` fulfillment mode exists because a real PACS may have no integration at all: staff apply the change and confirm it.

## 0.3 F2: customer networks → agent, outbound only, long-poll

**Golden rule:** nothing in the cloud ever opens a connection *into* the customer network (SPEC §1).
So something must live inside: the **connector agent**, a small .NET worker, one per PACS instance, that *calls out*.

How does a server send commands to something it cannot call? The agent asks. Options (ADR-003):

| Option | Why not / why |
|---|---|
| WebSocket / SignalR | Corporate TLS-inspecting proxies break upgrades and cut idle sockets. |
| gRPC streaming | Needs HTTP/2 end to end; proxies downgrade. |
| Server-Sent Events | One-way, buffered by proxies. |
| **HTTPS long-poll** ✔ | `POST /connector/v1/poll` waits up to 25 s and returns as soon as a command exists. Works through any proxy that allows HTTPS; any gateway replica can answer; the agent only asks when it has capacity. |

Identity of the agent: **mutual TLS** with a per-agent client certificate, issued at enrolment from a one-time token
(only its hash is stored). That is why `connector_registrations` and `connector_authorities` are in the catalog: the
gateway sees a certificate before it knows the tenant.

Commands are not a RabbitMQ queue but a **table** (`connector_commands`, ADR-005), because the agent needs things a
queue does badly: strict order per person (grant before revoke, `partition_key` + `sequence`), at most one in flight
per partition, leases and redelivery when an agent dies mid-command, priority lane for emergency revoke and lockdown,
and survival across hours of agent downtime and gateway restarts.

The **Connector Gateway** is its own process (ADR-004): it is the only internet-facing endpoint for agents, has its own
auth (mTLS) and a different load profile (many waiting polls), so it should not share a process with the user API.

## 0.4 F3: different vendors → one adapter contract

The agent hosts exactly one `IPacsAdapter`. The contract (`docs/contracts/adapter-interface.md`) is what Gatehouse
needs from any PACS: grant/revoke access, cards, door commands, events, export. Two simulated vendors are deliberately
opposite (SPEC §8): **LegacyPACS** = an SQL "integration table" polled by the vendor; **CloudPACS** = modern REST +
webhooks. If one contract fits both, it will fit real ones. What a vendor cannot do is declared as **capabilities**
(`pacs_instances.capabilities`), and the UI greys out what is unsupported instead of failing at runtime.
A conformance suite (`tests/Gatehouse.ContractTests`) runs the same tests against every adapter. The agent depends only
on `Gatehouse.Connector.Contracts`, never on the platform code, so it can ship to customers separately.

## 0.5 F4: many tenants, own IdPs → isolation and SSO

**Data isolation (ADR-001).** Three options: shared tables with `tenant_id`, schema per tenant, database per tenant.
Shared tables are cheapest, but one forgotten filter or raw query leaks another company's people, cards and movements.
Database per tenant makes a leak physically impossible on that connection and gives per-tenant backup, export,
retention and offboarding (`DROP DATABASE`), and lets a hybrid customer host its own DB. Cost: migrations run N times,
N connection pools; fine for tens of tenants.

**Where the tenant comes from.** Only from something the user cannot forge: the `tenant_id` claim in the signed token
(users), the client certificate (agents), the `gh-tenant-id` header set by our own code (messages). Never from the URL
or the body. Shared infrastructure (RabbitMQ, Redis, SignalR) is isolated logically by that id.

**SSO (ADR-002).** Each customer has its own IdP: Nordlys uses OIDC (Entra-like), Bricks uses SAML. The app should
not speak N protocols, so **Keycloak is the broker**: one realm, one Keycloak *Organization* per tenant, login by email
domain (`anna@nordlys.test` → Nordlys IdP). The app only speaks OIDC to Keycloak. A mapper stamps `tenant_id` on every
user from that IdP. Realm-per-tenant was rejected: N issuers to trust and N login URLs to choose from before you know
who the user is.

**Tokens never reach the browser.** Next.js is a BFF: it does the code flow server-side, keeps tokens in Redis and gives
the browser only an `HttpOnly` session cookie; browser calls go through `/api/proxy`. An XSS bug cannot steal a token.

**Roles are not in Keycloak.** Keycloak answers "who are you and which company"; Gatehouse answers "what may you do".
Manager and area owner are not roles at all, they are relations in the data (`persons.manager_id`, `area_owners`), so
they follow the org chart automatically and can be tested without Keycloak.

## 0.6 F5: security decisions → rules enforced by the server and the database

- **Approval chain by security level** (`security_levels.approver_chain`): low-risk rooms need the manager, sensitive
  ones the manager *and* the area owner, in that order. The manager knows the person; the owner knows the room.
- **No self-approval** and **admin ≠ approver**: an admin who could approve their own access defeats the point. Enforced in
  the domain and in authorization handlers, with tests (`authorization.md`).
- **Optimistic concurrency** (`xmin`) so two decisions at once cannot silently overwrite each other.
- **Audit** written in the same transaction as the decision, append-only (triggers), and hash-chained so editing history
  is detectable.
- **Secrets**: PIN encrypted, tokens hashed, card numbers hashed in events, never logged.
- **Lifecycle** (M8): eligibility end and termination revoke automatically; externals need a sponsor and an end date.

## 0.7 Reliability: the outbox

One user action often has follow-ups in other places (approve → create fulfillments → send command → notify).
If the database commit succeeds and the message send fails (or the reverse), the system lies. The **transactional
outbox** (ADR-005) writes the message into `outbox_messages` in the same transaction as the change; a relay publishes
it to RabbitMQ afterwards and retries until it succeeds. Consumers record `processed_messages` so a duplicate delivery
does nothing. MassTransit's own outbox was not used because it assumes one database, and we have one per tenant.

## 0.8 Shape of the code: modular monolith

Microservices would add network hops and distributed transactions before the domain is even understood (ADR-004).
Only two boundaries are real from day one: the gateway (public, mTLS) and the agent (other network). So: one codebase,
modules as folders (`Sites`, `People`, `Access`, `Fulfillment`, …), one transaction per use case, and a few hosts
(`Api`, `Workers`, `ConnectorGateway`, `Cli`, `Agent`). A module writes only its own data; cross-module effects go through
events. No MediatR or AutoMapper: plain handler classes registered explicitly, so the flow is readable.

## 0.9 Smaller decisions with the same logic

| Decision | Reason |
|---|---|
| Door events in daily Postgres partitions (ADR-006) | High-volume time series; retention = drop a day, instantly; no extra database product. |
| Live door state and occupancy in Redis | Read constantly, rebuildable from events; not worth a DB query per render. |
| Map as plain SVG (ADR-007) | Every area is a real DOM element: keyboard, screen reader, Playwright selectors; state shown by icon + label + hatch, never color alone; state colors cannot be themed away. |
| Strings for enums, UUID v7 ids, UTC everywhere | Readable data, index-friendly ids, no time-zone bugs in rules. |

## 0.10 Order of construction (SPEC §15)

The milestones follow the risk, not the screens:

1. **M1 Skeleton**: hosting, catalog + tenant DBs, sign-in for both tenants (OIDC and SAML). If SSO and tenancy don't
   work, nothing else matters.
2. **M2 Domain + map**: sites, areas, requests, approval routing, no self-approval.
3. **M3 Manual fulfillment**: desired vs confirmed with staff confirming by hand, audit chain. The governance value
   exists before any integration.
4. **M4 Event backbone + agent**: outbox, gateway, long-poll, first vendor. ADR-008 kept M3 side effects in-process until this existed.
5. **M5 Live building**: door events, SignalR floor plan, alarms, lockdown.
6. **M6 Second vendor**: proves the adapter contract.
7. **M7 Reconciliation**: closes the "was it really applied" loop.
8. **M8 Lifecycle**: HR sync, termination, sponsors, guests, cards, PIN.
9. **M9 Polish**: theming, GDPR retention, telemetry, full e2e suite.
