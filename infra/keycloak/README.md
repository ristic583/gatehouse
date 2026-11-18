# Keycloak (local development)

The AppHost starts Keycloak 26 (`keycloak`, http://localhost:8080) and imports every file in
`infra/keycloak/realms/` at startup (`start-dev --import-realm`). There is **no data volume**: the
realm is re-imported from these files on every start, so the committed JSON is the source of truth.
Change the realm by editing the JSON, not the admin console.

## What is imported

`realms/gatehouse-realm.json` - realm `gatehouse` (ADR-002):

- Organizations enabled; organizations `nordlys` ("Nordlys A/S", domain `nordlys.test`) and
  `bricks` ("Bricks GmbH", domain `bricks.test`).
- Client `gatehouse-web`: confidential, Authorization Code + PKCE (S256), redirect
  `http://localhost:3000/auth/callback`, default scopes `basic`, `profile`, `email`, `web-origins`,
  `gatehouse-tenant`, `gatehouse-api-audience` (`openid` is implicit in OIDC requests).
- Client `gatehouse-api`: bearer-only resource.
- Client scope `gatehouse-tenant`: maps user attribute `tenant_id` to the `tenant_id` claim
  (access token, ID token, userinfo).
- Client scope `gatehouse-api-audience`: adds `gatehouse-api` to the access token `aud`.
- User profile: unmanaged attributes are `ADMIN_EDIT`, so IdP mappers can write `tenant_id`.
- Token lifetimes: access token 300 s, SSO session idle 1800 s.

Because the realm file declares `clientScopes`, Keycloak does not create its built-in scopes; the
file therefore also declares `basic`, `profile`, `email` and `web-origins`.

`realms/gatehouse-platform-realm.json` - realm `gatehouse-platform` for platform operators (M10, ADR-009 §1):

- Local users only, brute-force protection on, OTP policy TOTP HmacSHA1, 6 digits, 30 s;
  `CONFIGURE_TOTP` is a default required action, so every new operator enrols OTP at first sign-in.
- Client `gatehouse-platform-web`: confidential, Authorization Code + PKCE (S256), redirect
  `http://localhost:3000/platform/auth/callback`, post-logout `http://localhost:3000/platform`,
  default scope `gatehouse-platform-api-audience`.
- Client `gatehouse-platform-api`: bearer-only resource; scope `gatehouse-platform-api-audience` adds it to `aud`.
- Built-in `admin-cli` is overridden with direct-access grants disabled: no client of this realm accepts
  password grants, so OTP cannot be bypassed.
- Dev user `operator@platform.test` (password `${GH_PLATFORM_OPERATOR_PASSWORD}`) with an imported OTP
  credential whose raw secret is `${GH_PLATFORM_OPERATOR_OTP_SECRET}`. Authenticator apps and the e2e fixture
  use **base32 of the secret's ASCII bytes** as the TOTP key (Keycloak's convention).
- The API accepts these tokens only on `/api/v1/platform/*` (scheme `PlatformBearer`); the CLI `bootstrap`
  creates the matching catalog `platform_operators` row from `Platform:BootstrapOperatorEmail`.

Realm `gatehouse` sends mail (invitations, password setup) via SMTP `mailpit:1025` from
`noreply@gatehouse.test`. The AppHost runs Mailpit (`mailpit`); read the mails at http://localhost:8025.

Client `gatehouse-provisioner` (realm `gatehouse`, M10-03): confidential, service account only (secret
`${GH_PROVISIONER_CLIENT_SECRET}`), used by `KeycloakIdentityProvisioner` for organizations, invited admins and
tenant IdPs. Service-account roles (`realm-management`): `manage-identity-providers`, `manage-users`, `view-realm`,
`manage-realm` - Keycloak 26.6 answers 403 on the organizations API without `manage-realm` (verified).
A domain owned by another organization is refused with HTTP 400 (mapped to `tenant.email_domain_taken`).

## Upstream test IdPs

A second Keycloak (`upstream-idp`, http://localhost:8081) imports `infra/keycloak/upstream/` and plays
the customers' identity providers:

- `nordlys-oidc` - OIDC provider with confidential client `gatehouse-broker`. Brokered into
  `gatehouse` as IdP `nordlys-oidc` (PKCE, `client_secret_post`, signature validated via JWKS).
  The browser uses `http://localhost:8081`; the token and JWKS calls go container-to-container via
  `UPSTREAM_IDP_INTERNAL_URL`. `KC_HOSTNAME=http://localhost:8081` keeps the issuer stable.
- `bricks-saml` - SAML 2.0 IdP whose SP client id is the broker's entity id
  `http://localhost:8080/realms/gatehouse`; NameID format email, POST binding, signed responses,
  attributes `email`, `firstName`, `lastName`. Brokered as IdP `bricks-saml`.

- `acme-oidc` - "the new customer's IdP" for tests/e2e only: client `gatehouse-broker-acme` (secret
  `${GH_ACME_BROKER_CLIENT_SECRET}`, redirect `http://localhost:8080/realms/gatehouse/broker/*`), no committed users,
  **not** brokered in the realm file (tenants connect it through the admin UI/API).

Both IdPs are linked to their organization (`kc.org.domain`, redirect when the email domain
matches) and carry a hardcoded `tenant_id` mapper (`nordlys` / `bricks`, sync mode `FORCE`).

**Dev only:** all three realms use `sslRequired: none` because the broker reaches the upstream IdP
over plain HTTP on the container network (`UPSTREAM_IDP_INTERNAL_URL`), which Keycloak does not treat
as a local address. Production realms must require TLS.

**Dev only:** the `bricks-saml` broker has `validateSignature: false` and `wantAssertionsSigned: false`
so the test IdP's keys need not be copied into the realm file. A real tenant IdP must have signature
validation enabled with its signing certificate.

Demo users and their intended roles: [demo-users.md](demo-users.md).

## Secrets

Secrets are never committed. The realm JSON uses placeholders (`${GH_WEB_CLIENT_SECRET}`, `${GH_BROKER_CLIENT_SECRET}`,
`${GH_DEMO_USER_PASSWORD}`, `${UPSTREAM_IDP_INTERNAL_URL}`, `${GH_PLATFORM_WEB_CLIENT_SECRET}`,
`${GH_PLATFORM_OPERATOR_PASSWORD}`, `${GH_PLATFORM_OPERATOR_OTP_SECRET}`, `${GH_PROVISIONER_CLIENT_SECRET}`) that
Keycloak resolves from environment variables at import time. The AppHost generates the values as
Aspire parameters (`web-client-secret`, `broker-client-secret`, `demo-user-password`, `platform-web-client-secret`,
`platform-operator-password`, `platform-operator-otp-secret`, `provisioner-client-secret`; secret, persisted) and stores them in the AppHost user
secrets:

```bash
dotnet user-secrets list --project src/Gatehouse.AppHost
```

## Admin console

Open http://localhost:8080/admin. The admin user name and password are the Aspire-generated
`keycloak-password` parameter: find them in the Aspire dashboard (resource `keycloak` →
environment `KC_BOOTSTRAP_ADMIN_USERNAME` / `KC_BOOTSTRAP_ADMIN_PASSWORD`) or in the AppHost user secrets.
