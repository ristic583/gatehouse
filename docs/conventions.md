# Conventions

Binding rules for every change. They extend `CLAUDE.md` (C#, TypeScript, Logging, Tenancy, Tests); where both say
something, `CLAUDE.md` wins. A change that breaks a rule here either fixes it in the same change or records an exception in
the PR description. Existing drift is fixed where it is, not copied.

## 1. Layers and placement

- Dependencies point inward only: Api → Application → Domain; Infrastructure implements Application interfaces. Domain has
  no I/O, no `TimeProvider`, no logging.
- Api endpoints are thin: bind request, call one handler, `ToHttpResult()`. No business rules, no `ITenantDb`, no
  `DbContext` in `src/Gatehouse.Api`.
- New code goes into an existing module (`CLAUDE.md` → C#). A new module needs an ADR line in `docs/adr/` and a codemap entry.
- Agent, gateway and adapters: logic that decides whether a PACS mutation may run lives in the agent executor, not in an
  adapter. Adapters only translate.

## 2. Naming in Application

| Kind | Name | Shape |
|---|---|---|
| Use case (one action) | `{Verb}{Noun}Handler` in `{Module}/{UseCase}/` | `HandleAsync(command, ct) → Result<T>`; one public method |
| Shared domain workflow used by **two or more** handlers | `{Noun}Service` | no HTTP knowledge; returns `Result` |
| Read side reused by several handlers | `{Noun}ReadModel` / `{Noun}Store` | queries only, `AsNoTracking()` |
| Background loop body | `{Noun}Runner` | `RunAsync`/`RunTenantAsync` |

- One handler per use case. Classes named `{X}Handlers` holding several actions are not allowed in new code.
- A `Service` that is used by one handler only is merged into that handler.
- Commands/queries are records named `{UseCase}Command` / `{UseCase}Query`; DTOs end in `Dto`.

## 3. Validation and errors

- Request shape (required, length, format, ranges of a single request) → FluentValidation `{UseCase}Validator`, registered
  with the module. Handlers do not repeat shape checks.
- Business invariants (state transitions, windows, uniqueness meaning) → Domain methods returning `Result`.
- Existence and permission → handler (`NotFound` before `Forbidden` where existence must not leak, per `authorization.md`).
- Error codes are `{module}.{snake_case}` and listed in `docs/contracts/domain-model.md` §4 before use.
- `throw` only for bugs and infrastructure. `catch (Exception)` only at host boundaries (endpoint filter, worker loop, agent
  loop) and always logs and either rethrows or records a failure; every `#pragma warning disable CA1031` carries a one-line
  comment why.

## 4. Time, order and concurrency (physical-access invariants)

These rules exist because every serious defect found on 2026-11-15 broke one of them.

1. **Windows are half-open** `[start, end)` in UTC; display converts to the site time zone. A grant is never applied before
   `start` or after `end`, regardless of PACS capabilities (enforce at dispatch and in the agent).
2. **Revoke what was granted**: a revoke targets the vendor level recorded with the grant, never the current mapping.
3. **Never shrink current access by accident**: a renewal or overlapping grant extends the PACS window; it never replaces it
   with a later one.
4. **Every end sends a revoke**: expiry, early end, termination and card deactivation each produce explicit PACS commands;
   validity windows in the PACS are a second line, not the only one.
5. **Interactive physical actions carry a deadline** (`ExecuteBy`); late commands are refused at gateway and agent.
6. **Superseded is final**: a command superseded or revoked is never redelivered, also after a retry or lease expiry.
7. **Invariants that span rows are checked and written atomically**: unique index, `SELECT … FOR UPDATE`, or the tenant
   advisory lock. A count-then-write in two steps is not allowed.
8. **Side effects after commit**: messages, SignalR updates, Redis projections and occupancy happen after the database
   transaction commits and are idempotent by a stable event id.
9. **Projections never move backwards**: a stored state is only replaced by an event with a newer timestamp (atomic compare).
10. **A task is resolved only when the effect is confirmed** (connector acknowledgement or verified absence), not when the
    command is queued.
11. Each rule above that a change touches gets a regression test of the scenario.

## 5. Persistence and messaging

- Migrations: one per change, named for the intent (`PendingRequestUnique`), never edited after merge; a migration that adds
  a unique constraint states how existing duplicates are handled.
- Consumers swallow only the dedup conflict on `processed_messages`; every other exception fails the message.
- Redis keys `tenant:{tenantId}:…`; writes that compete use Lua/compare-and-set, not get-then-set.

## 6. Security

- Every new read endpoint: cross-tenant 404 test. Every new write endpoint: forbidden-role test.
- Authorization is re-evaluated at the time of use (PIN reveal, live feed, task actions), not only when a claim/task/
  subscription is created.
- URLs that come from tenant configuration (IdP metadata, webhooks) pass the SSRF checks of `IdpMetadataFetcher`; production
  exceptions go through explicit configuration (`Identity:AllowedInsecureHosts`, `Keycloak:BackchannelRewrites`).
- Sign-in transactions are bound to the browser (`gh_login`); session writes never recreate a deleted session.

## 7. Size and readability

- C# file ≤ 300 lines; > 400 must be split (by responsibility, not by `partial`). Method ≤ 60 lines.
- Web component file ≤ 250 lines; split into subcomponents/hooks above that.
- Line length ≤ 140 (C# and TS). No compressed one-line components; Prettier formats web code.
- Comments explain *why*; no commented-out code; no TODO without a linked plan item.

## 8. Web

- Product features only in `src/web/features/{feature}`; design references, previews and the "how it works" scene live under
  `src/web/features/showcase/` and are not imported by product features.
- Data access per feature in `api.ts` + query keys object; mutations invalidate the feature's keys; no `fetch` in components.
- Every page handles four states explicitly: loading, empty, error (with retry), and not permitted (403 → message, no retry).
- Responsive: every page works at 375 px without horizontal page scroll (tables become cards or scroll inside their box).
- User-facing text never shows ids; show names, dates and areas.

## 9. Tests

- A bug fix starts with a failing test that reproduces it.
- Scenario tests for physical access run on the simulators and assert the PACS state and a swipe decision.
- E2E runs only after the system is ready (`warmup.setup.ts` must wait for API, Keycloak and seed data, not only the web
  port); specs never depend on data left by other specs.

## 10. Docs and change hygiene

- Contract first: a new endpoint, message, error code or table is added to `docs/contracts/` in the same change.
- `docs/codemap.md` updated for every new public type, endpoint, table or fixture.
- Decisions that change a rule here → ADR + update this file in the same change.
- One logical change per commit; message one line + attribution.
