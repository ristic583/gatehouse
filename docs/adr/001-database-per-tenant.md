# ADR-001: Database-per-tenant with a catalog database

- **Status:** Accepted
- **Date:** 2026-11-09
- **Spec refs:** §2, §4, §16 (tenancy acceptance tests)

## Context

Gatehouse is multi-tenant. Tenants are companies that hold personal data (people, cards, photos, PINs, door movements) and expect strong isolation, per-tenant retention (GDPR, §10) and, for the hybrid and isolated deployment models, the ability to lift one tenant out and run it elsewhere. The options were:

1. **Shared schema** with a `tenant_id` column on every table and a global query filter.
2. **Schema-per-tenant** in one database.
3. **Database-per-tenant** plus a small catalog database.

## Decision

Use **one PostgreSQL database per tenant** plus one **catalog database**.

- The **catalog DB** (`CatalogDbContext`) holds `Tenant` rows (id, status, database name, encrypted connection string, IdP kind, Keycloak organization alias) and, from M4, `ConnectorRegistration` rows. Nothing else.
- Each **tenant DB** (`TenantDbContext`) holds all domain data for that tenant. Tables carry **no** `tenant_id` column; the database itself is the boundary.
- The tenant id is a lowercase slug (`TenantId`, regex `^[a-z][a-z0-9-]{2,31}$`, e.g. `nordlys`, `bricks`). The tenant database name is `gh_tenant_{slug}` with `-` replaced by `_`.
- Per request the tenant is resolved **only** from the `tenant_id` claim in the access token (see ADR-002) by `TenantResolutionMiddleware`, which loads the tenant from the catalog (cached for 60 s), decrypts the connection string with `ISecretProtector` and sets the scoped `ITenantContext`. `TenantDbContext` is built from `ITenantContext` and refuses to open a connection when no tenant is set (`TenantNotResolvedException`).
- Background code (workers, gateway, CLI) uses `ITenantScopeFactory.CreateScopeAsync(TenantId)` to get a DI scope bound to one tenant. Code that touches several tenants loops over `ICatalogReader.GetActiveTenantsAsync()` and opens one scope per tenant.
- Migrations: one EF Core migration set for the catalog, one for tenants. `gatehouse-cli migrate --all` applies the catalog migrations and then the tenant migrations to every tenant in the catalog. Tenant DBs are created by `gatehouse-cli tenant create`.
- Connection pooling: Npgsql pools per connection string, so each tenant gets its own pool. `Maximum Pool Size` per tenant is set to 20 in the generated connection string.

## Consequences

**Positive**
- Physical isolation: a missing filter cannot leak another tenant's rows. Isolation tests become simple (two tenants, two DBs, assert zero overlap).
- Per-tenant backup/restore, retention, export and offboarding (`DROP DATABASE`).
- Hybrid/isolated deployments can host one tenant's DB on the customer side unchanged.
- No `tenant_id` noise in domain tables or indexes.

**Negative**
- Migrations must run N times; a failing tenant migration must not block others (the CLI reports per-tenant results and exits non-zero if any failed).
- More connection pools; acceptable at portfolio scale (tens of tenants). At hundreds of tenants we would add PgBouncer.
- Cross-tenant reporting needs fan-out queries (not a product requirement).
- Shared infrastructure that is *not* per-tenant (RabbitMQ, Redis, SignalR) still needs logical isolation: every message carries `TenantId` (ADR-005), every Redis key starts with `tenant:{tenantId}:`, every SignalR group starts with `tenant:{tenantId}:`.

## Alternatives considered

- **Shared schema + global query filter:** cheapest, but one forgotten `IgnoreQueryFilters()` or raw SQL query leaks data, and per-tenant retention/export is harder. Rejected given the sensitivity of the data.
- **Schema-per-tenant:** similar isolation to DB-per-tenant inside one DB, but EF Core handles a dynamic default schema poorly (model caching per schema, migrations history per schema) and it gives none of the backup/offboarding benefits. Rejected.
