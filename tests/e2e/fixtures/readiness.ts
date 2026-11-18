import { expect, type APIRequestContext, type Browser } from "@playwright/test";
import { demoUsers } from "./demo-users";
import { signIn } from "./sign-in";

/** API base URL (the AppHost's `api` resource, launch profile `http`). */
export function apiUrl(): string {
  return (process.env.GATEHOUSE_API_URL ?? "http://localhost:5048").replace(/\/+$/, "");
}

/** Keycloak base URL (AppHost resource `keycloak`). */
export function keycloakUrl(): string {
  return (process.env.GATEHOUSE_KEYCLOAK_URL ?? "http://localhost:8080").replace(/\/+$/, "");
}

const intervals = [1_000, 2_000, 5_000];

/** API `/health` answers 200 (the API waits for the migrator, so the catalog and seed have run). */
export async function waitForApiHealth(request: APIRequestContext, timeout: number): Promise<void> {
  await expect
    .poll(async () => (await request.get(`${apiUrl()}/health`, { timeout: 10_000 }).catch(() => null))?.status() ?? 0, {
      timeout,
      intervals,
    })
    .toBe(200);
}

/** Realm `gatehouse` publishes its discovery document with an issuer. */
export async function waitForKeycloakDiscovery(request: APIRequestContext, timeout: number): Promise<void> {
  await expect
    .poll(
      async () => {
        const discovery = `${keycloakUrl()}/realms/gatehouse/.well-known/openid-configuration`;
        const response = await request.get(discovery, { timeout: 10_000 }).catch(() => null);
        if (response?.status() !== 200) return "";
        return ((await response.json()) as { issuer?: string }).issuer ?? "";
      },
      { timeout, intervals },
    )
    .toMatch(/\/realms\/gatehouse$/);
}

/** One demo user signs in through the BFF and sees at least one seeded site (`/api/proxy/v1/sites`). */
export async function waitForSignedInDataCall(browser: Browser, timeout: number): Promise<void> {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await expect(async () => {
      await signIn(page, demoUsers.annaLarsen);
      const response = await page.request.get("/api/proxy/v1/sites");
      expect(response.status()).toBe(200);
      expect(((await response.json()) as unknown[]).length).toBeGreaterThan(0);
    }).toPass({ timeout, intervals });
  } finally {
    await context.close();
  }
}
