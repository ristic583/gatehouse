# ADR-009: Platform operator realm and runtime tenant provisioning

- **Status:** Accepted
- **Date:** 2026-11-14
- **Spec refs:** §3, §14 (M10 = tenant onboarding + admin)
- **Relates to:** ADR-001 (database per tenant), ADR-002 (one realm with Organizations; this ADR supersedes only its "mappers are created only from the committed realm export" mitigation)

## Context

Until M9 a tenant exists only through the CLI (`bootstrap`, `tenant create`, `connector create`), seed data and the committed realm file `infra/keycloak/realms/gatehouse-realm.json`. M10 must let a newly signed customer be onboarded entirely from the UI: create the tenant (catalog row, tenant database, Keycloak organization, first tenant admin), then let that admin connect their identity provider, model sites/floors/areas, register PACS instances and connectors. That needs (a) an operator role above all tenants and (b) Keycloak changes at runtime.

The operator must never be derivable from a tenant token: a tenant admin of `nordlys` who could forge or be granted "platform" rights inside the `gatehouse` realm could read or create other tenants.

## Decision

**1. Separate realm for platform operators.**
- Keycloak realm `gatehouse-platform` (committed infrastructure file `infra/keycloak/realms/gatehouse-platform-realm.json`, created once, not per tenant). Local users only, no identity brokering, no `tenant_id` mapper, OTP required for every user (`CONFIGURE_TOTP` required action; dev operator gets a TOTP credential whose secret is the Aspire parameter `platform-operator-otp-secret`).
- Clients: `gatehouse-platform-web` (confidential, Authorization Code + PKCE, redirect `http://localhost:3000/platform/auth/callback`) and bearer-only `gatehouse-platform-api` (audience mapper).
- The API registers a second JWT scheme `PlatformBearer` (authority `http://localhost:8080/realms/gatehouse-platform`, audience `gatehouse-platform-api`). It is **not** part of the default `Gatehouse` policy scheme, so a platform token authenticates nothing on tenant routes, and a tenant token fails `PlatformBearer` on platform routes.
- Authorization: policy `PlatformOperator` = scheme `PlatformBearer` + `PlatformOperatorRequirement` (an `Active` row in catalog table `platform_operators`, linked by `sub`, first by email). Keycloak still holds no Gatehouse roles (ADR-002 principle kept).
- BFF: separate OIDC client and a separate cookie `gh_platform_session` (`__Host-gh_platform_session` over HTTPS); a platform session can only call `/api/proxy/v1/platform/*`, a tenant session never can (the API enforces it as well).

**2. Platform routes never open a tenant database.** `/api/v1/platform/*` reads and writes the catalog only. Tenant figures shown to operators come from catalog table `tenant_statistics`, written by the Workers job `TenantStatisticsJob`, which loops `GetActiveTenantsAsync` → `CreateScopeAsync` (CLAUDE.md tenancy rule 3) and stores **counts only, no personal data**. So tenancy rule 1 stays absolute: tenant data is reached only with a tenant from a claim, a certificate or a message header. The `tenantId` in a platform route identifies a catalog row, never a tenant scope.

**3. Provisioning runs in Workers as a resumable step machine.** `POST /api/v1/platform/tenants` writes `tenants` (status `Provisioning`) and `tenant_provisioning`, and returns `202`. `TenantProvisioningJob` (Workers, every 5 s) runs `TenantProvisioningRunner` steps in order, each idempotent: `Database` → `Migrate` → `Baseline` (default security levels, settings) → `FirstAdmin` (Employee person + `TenantAdmin` role assignment) → `Organization` (Keycloak organization + verified email domain) → `AdminInvite` (Keycloak local user with `tenant_id` attribute + execute-actions email `UPDATE_PASSWORD`) → `Completed` (status `Active`). A failure stores the step and a secret-free error (`ProvisioningFailed`); `POST .../provisioning/retry` resumes at the failed step.

**4. Keycloak admin API through a port.** `Application/Tenancy/IIdentityProvisioner` with `Infrastructure/Identity/KeycloakIdentityProvisioner` (typed `HttpClient`, client-credentials token of confidential client `gatehouse-provisioner` in realm `gatehouse`, secret = Aspire parameter `provisioner-client-secret`, only the realm-management roles the calls need). Integration tests use `FakeIdentityProvisioner`; one AppHost test hits the real Keycloak.
- Everything that decides tenancy is server-generated: organization alias = tenant id, IdP alias = `{tenantId}-oidc` / `{tenantId}-saml`, hardcoded `tenant_id` mapper (sync mode `FORCE`) with the value taken from `ITenantContext`, never from request input. A tenant admin can only touch the IdP of the organization equal to their own `tenant_id` claim.
- Email domains are set by the platform operator at creation and cannot be changed by the tenant admin (prevents claiming another tenant's domain). A domain already owned by another organization → `409 tenant.email_domain_taken`.
- SAML providers are always created with signature validation on (`validateSignature: true`, `wantAssertionsSigned: true`); the dev-only relaxation of `bricks-saml` (infra README) is never available through the API.
- Fetching tenant-supplied metadata/discovery URLs (test connection) goes through `IdpMetadataFetcher`: HTTPS only, no redirects, 5 s timeout, 1 MiB limit, private/loopback/link-local addresses refused, except hosts listed in `Identity:AllowedInsecureHosts` (Development: `localhost:8081`, `upstream-idp`).

**5. Dev mail.** The AppHost adds a Mailpit container (`mailpit`, SMTP 1025, UI/API 8025) and the realm `gatehouse` sends Keycloak emails through it. E2E reads invites from the Mailpit API.

**6. Platform audit.** Catalog table `platform_audit_entries`, append-only (trigger) and hash-chained like the tenant audit (`AuditHasher`), for every platform write.

## Consequences

**Positive**
- A customer is onboarded without CLI, seed data or realm-file edits; the realm files only hold the two realms, clients and the demo tenants.
- Operator privilege is cryptographically separate (different issuer + audience), MFA-only, and cannot leak into tenant routes.
- Platform operators see no personal data from tenant databases.

**Negative**
- Keycloak becomes partly runtime-configured: the demo tenants still come from the realm export, new tenants from the API. Reset instructions (`docker volume rm gatehouse-keycloak-data ...`) wipe runtime-created organizations too; the catalog keeps them, so `provisioning/retry` of the `Organization` step must be able to re-create them (follow-up: a "re-sync identity" platform action).
- Statistics are up to ~5 min old.
- A second issuer for the API to trust (still static, unlike realm-per-tenant).

## Alternatives considered

- **Platform role inside realm `gatehouse`** (realm role or a special organization): one compromised or misconfigured mapper could grant cross-tenant power; rejected.
- **Realm per tenant at onboarding:** contradicts ADR-002 and needs dynamic issuer validation; kept only for the isolated deployment (M11).
- **Platform API opening tenant scopes from the route id:** breaks tenancy rule 1; rejected in favour of the statistics job.
- **Synchronous provisioning in the request:** Keycloak and `CREATE DATABASE` latency plus partial failures make a resumable background run safer.
