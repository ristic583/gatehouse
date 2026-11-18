# Public demo stack

The whole of Gatehouse in Docker Compose, as it runs at the live demo: production builds of the API, Workers, Connector
Gateway, CLI and web app, two demo tenants (Nordlys A/S with an OIDC identity provider, Bricks GmbH with SAML), two
simulated PACS with their connector agents and a simulated building that generates door traffic. Everything sits behind
one HTTP entry (Caddy):

| Path | Service |
|---|---|
| `/` | Next.js web app (backend-for-frontend) |
| `/kc` | Gatehouse's Keycloak (realms `gatehouse` and `gatehouse-platform`) |
| `/idp` | The demo companies' own identity providers (realms `nordlys-oidc`, `bricks-saml`) |

Keycloak admin consoles, admin APIs and `master` realms answer 404 from outside.

## Start

Needs Docker with Compose, Python 3 and OpenSSL.

```bash
deploy/demo/setup.sh
```

The first run writes `deploy/demo/.env` with generated secrets, renders the Keycloak realms for the public URL into
`deploy/demo/data/`, creates the connector gateway's certificate, builds the images and starts the stack. The first start
takes a few minutes while Keycloak imports its realms and the migrator seeds the tenants. Then open
<http://localhost:8088> and pick a person.

## Warm-up

The seed records some access as already confirmed, but the simulated PACS start empty, so the first reconciliation
reports it as "Missing in PACS". `warmup.sh` (run by `setup.sh` and `reset.sh`) signs in as the security officer and
remediates those findings through the API, exactly as a person would in the Control center. The connector agents then
grant the access in the PACS and the simulated building starts walking people through its doors.

## One-click personas

The login page lists demo users (`src/web/lib/auth/demo-personas.ts`). Choosing one posts to `/auth/demo`; the web
server then walks the normal Authorization Code + PKCE sign-in itself, through Gatehouse's Keycloak and the company's own
identity provider, with the generated demo password from `.env`. The visitor never sees a password and the identity
flow is the real one. `GATEHOUSE_DEMO_URL_MAP` lets the web container make those requests on the Docker network instead
of through the public URL.

The feature is off unless `GATEHOUSE_DEMO_SIGN_IN=true`; only the listed personas can be used.

## Behind a public address

Set the address before the first run (it is written into `.env` and the realms):

```bash
PUBLIC_URL=https://demo.example.com HTTP_BIND=127.0.0.1 HTTP_PORT=8088 deploy/demo/setup.sh
```

Put a TLS-terminating reverse proxy in front of `HTTP_BIND:HTTP_PORT` that forwards the `Host` header. With an `https`
public URL, Caddy tells the apps that the original request was HTTPS (`FORWARDED_PROTO`).

Things worth doing for a public instance:

- run it on a machine of its own whose outbound traffic cannot reach your private networks: tenant admins can enter
  identity-provider URLs (the API refuses private addresses, but defence in depth is cheap).
  `sudo deploy/demo/egress-firewall.sh install` drops container traffic to private ranges, now and after every boot;
- rate-limit the entry at the proxy;
- reset the data every night.

## Reset

```bash
deploy/demo/reset.sh
```

Removes every volume (databases, queues, stored files, agent enrolments) and starts again from the seed. Secrets and the
gateway certificate stay. For a nightly reset add a cron entry such as `0 4 * * * /path/to/deploy/demo/reset.sh`.

## Change the public URL

Stop the stack, delete `.env` and `data/`, run `docker compose down -v` in this folder, then run `setup.sh` again with the
new `PUBLIC_URL`.
