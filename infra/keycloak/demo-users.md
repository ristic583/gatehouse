# Demo users

Every demo user lives in the upstream IdP container (`upstream-idp`, http://localhost:8081) and signs in
to Gatehouse through the broker in realm `gatehouse` (identity-first: type the email on the Gatehouse
login page, Keycloak redirects to the tenant's IdP by email domain).

**Password:** all demo users share one generated password, the AppHost parameter `demo-user-password`.
It is never committed. Read it with:

```bash
dotnet user-secrets list --project src/Gatehouse.AppHost
```

(key `Parameters:demo-user-password`). Playwright reads it from `GATEHOUSE_DEMO_PASSWORD`.

Intended roles match the M2-08 seed; until then every user is only a tenant member.

## Nordlys A/S (`nordlys`, OIDC, upstream realm `nordlys-oidc`)

| User | Email | Intended role |
|---|---|---|
| Anna Larsen | anna.larsen@nordlys.test | Employee |
| Mikkel Holm | mikkel.holm@nordlys.test | Manager |
| Sofie Berg | sofie.berg@nordlys.test | Area owner |
| Erik Møller | erik.moller@nordlys.test | Manager and area owner (top of the org) |
| Jonas Nielsen | jonas.nielsen@nordlys.test | Security staff |
| Freja Dahl | freja.dahl@nordlys.test | Tenant admin |
| Oliver Kruse | oliver.kruse@nordlys.test | External (sponsor Mikkel Holm) |

## Bricks GmbH (`bricks`, SAML 2.0, upstream realm `bricks-saml`)

| User | Email | Intended role |
|---|---|---|
| Lena Schmidt | lena.schmidt@bricks.test | Employee |
| Max Weber | max.weber@bricks.test | Manager |
| Clara Fischer | clara.fischer@bricks.test | Area owner |
| Jan Koch | jan.koch@bricks.test | Manager and area owner (top of the org) |
| Paul Wagner | paul.wagner@bricks.test | Security staff |
| Mia Becker | mia.becker@bricks.test | Tenant admin |
| Tom Richter | tom.richter@bricks.test | External (sponsor Max Weber) |
