# ADR-002: One Keycloak realm with Organizations (not realm-per-tenant), and a Next.js BFF

- **Status:** Accepted (the realm-export-only mapper mitigation is superseded by ADR-009, 2026-11-14)
- **Date:** 2026-11-09
- **Spec refs:** §3, §14, §16 (sign-in acceptance tests)

## Context

Each tenant signs in through its own upstream IdP: Nordlys A/S via OIDC (Entra-like), Bricks GmbH via SAML 2.0. Guests without an IdP get magic links (M8). The platform API needs one trustworthy `tenant_id` per token. Keycloak offers two ways to separate tenants:

1. **Realm-per-tenant:** each tenant has its own realm, clients, IdPs and signing keys.
2. **One realm with Organizations** (GA since Keycloak 26): organizations group members, own email domains and link identity providers; the login page routes users to the right IdP by email domain.

## Decision

Use **one realm named `gatehouse`** with **one Keycloak Organization per tenant**.

- Organization alias = tenant id (`nordlys`, `bricks`). Each organization owns its email domain (`nordlys.test`, `bricks.test`) and links exactly one upstream IdP.
- Upstream IdPs (local dev): a second Keycloak container named `upstream-idp` hosts two realms that play the customer IdPs:
  - `nordlys-oidc` - an OIDC provider, brokered in `gatehouse` as IdP alias `nordlys-oidc`.
  - `bricks-saml` - a SAML 2.0 IdP, brokered in `gatehouse` as IdP alias `bricks-saml`.
- Login is identity-first: the user types an email, Keycloak redirects to the IdP linked to the organization that owns the email domain (`kc.org.broker.redirect.mode.email-matches=true`).
- **`tenant_id` claim:** every IdP in `gatehouse` has a *Hardcoded Attribute* mapper (sync mode `FORCE`) that sets the user attribute `tenant_id` to the tenant slug. A realm-level client scope `gatehouse-tenant` contains a *User Attribute* protocol mapper that emits it as the `tenant_id` claim in access and ID tokens. Local users (guests, M8) get the attribute set by the platform when created. The API never trusts the `organization` claim for tenancy; `tenant_id` is the only source.
- **Clients in `gatehouse`:**
  - `gatehouse-web` - confidential client used by the Next.js BFF, Authorization Code + PKCE (S256), redirect URI `http://localhost:3000/auth/callback`, post-logout redirect `http://localhost:3000/`, default scopes include `gatehouse-tenant` and `gatehouse-api-audience`.
  - `gatehouse-api` - bearer-only resource; no flows enabled. Tokens for it carry `aud: gatehouse-api` via the `gatehouse-api-audience` client scope (Audience mapper).
- **Roles are not stored in Keycloak.** Keycloak authenticates and asserts tenant + identity (`sub`, `email`, names). All Gatehouse roles and scopes (manager, sponsor, area owner, security staff, tenant admin) live in the tenant database (see `docs/contracts/authorization.md`). This keeps "approval rights separate from admin rights" a server-side domain rule and lets tenant admins manage roles in the app.
- **BFF:** the browser never sees tokens. The Next.js server performs the OIDC code flow with `openid-client` (v6), stores tokens in a server-side session in Redis (key `web:session:{sessionId}`), and gives the browser only an opaque, `HttpOnly`, `Secure`, `SameSite=Lax` cookie named `__Host-gh_session` (`gh_session` without the `__Host-` prefix when served over plain `http://localhost`). Browser calls go to `/api/proxy/...` route handlers that attach the access token and refresh it when it expires in < 60 s.
- Ports in local dev are fixed so issuers are stable: `keycloak` on `http://localhost:8080`, `upstream-idp` on `http://localhost:8081`, web on `http://localhost:3000`. Issuer the API validates: `http://localhost:8080/realms/gatehouse`.

## Consequences

**Positive**
- One set of clients, one signing key set, one issuer for the API to validate. Adding a tenant = add an organization + IdP, no new client config in the apps.
- Email-domain routing gives a clean single login page for all tenants.
- Role logic is testable in plain .NET tests without Keycloak.

**Negative**
- Tenants share realm-level settings (password policy, token lifetimes, themes). Per-tenant login theming is limited to what organizations allow; app-level theming (§11) is unaffected.
- A misconfigured IdP mapper could assign the wrong `tenant_id`. Mitigation: mappers are created only from the committed realm export, and the API also checks that the tenant exists and is active.
- Organizations are relatively new in Keycloak; we pin Keycloak 26.x.

## Alternatives considered

- **Realm-per-tenant:** strongest isolation, but N issuers for the API to trust (dynamic issuer validation), N copies of client config, and N login URLs the BFF must choose between before knowing the user. Kept as an option for the isolated deployment model, where a single tenant runs its own Keycloak anyway.
- **Tokens in the browser (SPA with PKCE):** simpler, but violates the spec's "keeps tokens off the browser" requirement and exposes tokens to XSS.
