import { expect, test as setup } from "@playwright/test";
import { waitForApiHealth, waitForKeycloakDiscovery, waitForSignedInDataCall } from "./fixtures/readiness";
import { webUrl } from "./fixtures/sign-in";

/** API health, Keycloak discovery and one signed-in data call together get at most 4 minutes. */
const READINESS_MS = 240_000;

// Runs once before the parallel sign-ins (project `warmup`): `next dev` compiles routes on first request and the BFF
// discovers Keycloak lazily, so several cold sign-ins at once can outlast the redirect timeout.
setup("system is ready for sign-in", async ({ request, browser }) => {
  setup.setTimeout(READINESS_MS + 180_000);
  const deadline = Date.now() + READINESS_MS;
  const left = () => Math.max(deadline - Date.now(), 1_000);
  await waitForApiHealth(request, left());
  await waitForKeycloakDiscovery(request, left());
  // Seeded data reachable through a real sign-in: the specs never start against a half-ready system.
  await waitForSignedInDataCall(browser, left());

  await expect
    .poll(
      async () => {
        // Email-first sign-in: GET /auth/login shows the email screen, the POST starts OIDC with Keycloak.
        const response = await request.post(`${webUrl()}/auth/login`, {
          form: { email: "warmup@nordlys.test", returnTo: "/" },
          headers: { Origin: webUrl() },
          maxRedirects: 0,
          timeout: 30_000,
        });
        return response.headers()["location"] ?? `status ${response.status()}`;
      },
      { timeout: 120_000, intervals: [1_000, 2_000, 5_000] },
    )
    .toMatch(/\/realms\/gatehouse\//);

  // Compile the pages the sign-in passes through.
  await request.get(`${webUrl()}/`, { maxRedirects: 0, timeout: 60_000 });
  await request.get(`${webUrl()}/map`, { maxRedirects: 0, timeout: 60_000 });
});
