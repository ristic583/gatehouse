# ADR-008: In-process cross-module side effects until the event backbone exists (M3)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §5.1, §5.4, §7, §10, §15 (M3 = manual fulfillment, M4 = event backbone)

## Context

ADR-004 says cross-module side effects go through domain events (outbox → RabbitMQ → consumers, ADR-005). The outbox relay, MassTransit consumers and `processed_messages` handling arrive in M4. M3 already needs several cross-module follow-ups:

- an entitlement is approved → `Grant` fulfillments are created (already done in-process since M2-14);
- an entitlement is revoked → unapplied grants are superseded and `Revoke` fulfillments are created;
- a fulfillment fails → a `FulfillmentFailed` staff task is opened; a fulfillment is confirmed → its open tasks are resolved;
- a revocation stays unconfirmed → a `RevocationUnconfirmed` task is opened.

Building a temporary in-process event dispatcher would be thrown away in M4 and would hide transactional behaviour that M4 must change anyway.

## Decision

1. Until M4, these follow-ups run **in-process, in the same `SaveChangesAsync`** as the triggering change, through small application services owned by the target module: `FulfillmentPlanner` (Fulfillment module), `StaffTaskService` (Tasks module). Handlers call these services; **handlers still never call other handlers**.
2. Each call site carries a comment `// M4: move to consumer of {EventName}` naming the domain event that will drive it (`FulfillmentDesired`, `EntitlementRevoked`, `FulfillmentFailed`, `FulfillmentConfirmed`).
3. The domain events are still raised and written to `outbox_messages` exactly as `messages.md` specifies, so M4 only has to add consumers and delete the in-process calls.
4. **Audit entries are not a follow-up**: they are always written in the business transaction (domain-model §2.8), before and after M4.
5. Background checks that need every tenant (the unconfirmed-revocation monitor) run in `Gatehouse.Workers`, looping `ICatalogReader.GetActiveTenantsAsync()` + `ITenantScopeFactory.CreateScopeAsync()` (CLAUDE.md tenancy rule 3). This gives Workers their tenancy wiring before the M4 outbox relay needs it.

## Consequences

**Positive**
- One transaction per use case; M3 behaviour is easy to test and reason about.
- M4 has a precise, greppable list of call sites to convert into consumers.

**Negative**
- M3 handlers temporarily write aggregates of other modules (via those services), which ADR-004 otherwise forbids.
- When M4 moves the follow-ups to consumers they become eventually consistent; tests that assert on tasks/fulfillments right after an API call must then wait for the consumer (M4 planning must account for this).

## Alternatives considered

- **In-memory domain event dispatcher in M3:** same transactional semantics, more code, replaced in M4.
- **Pull the outbox relay forward into M3:** contradicts the milestone split (M4 = event backbone) and drags RabbitMQ consumers into a manual milestone.

## M4 outcome

- **Moved to consumers:** `FulfillmentFailed` → open `StaffTask(FulfillmentFailed)`; `FulfillmentConfirmed` → auto-resolve tasks; `FulfillmentDesired` → dispatch connector commands; `FulfillmentSuperseded` → supersede queued commands. These are eventually consistent; integration tests drain the outbox with `MessagingDriver.DrainAsync`.
- **Stays in-process by design:** grant fulfillment creation on approval (`StepDecisionService`) and revocation planning (`FulfillmentPlanner`). They must be atomic with the entitlement change and their ids go into the same audit entry (`entitlement.approved.fulfillmentIds`, `entitlement.revoked.revokeFulfillmentIds`). Their `// M4:` comments become `// Planned in-process by design (ADR-008, M4 outcome)`.
