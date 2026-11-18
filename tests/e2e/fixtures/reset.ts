import { expect, type Browser } from "@playwright/test";
import { storageStateFor } from "./auth-state";
import { webUrl } from "./sign-in";

/** Restores the seeded demo entitlements of a tenant through the dev-only reset endpoint (tenant admin session). */
export async function resetDemoAccess(browser: Browser, tenant: "nordlys" | "bricks", mode: "Manual" | "Connector" = "Manual"): Promise<void> {
  const context = await browser.newContext({ storageState: storageStateFor(tenant === "nordlys" ? "freja.dahl" : "mia.becker") });
  try {
    const response = await context.request.post(`${webUrl()}/api/proxy/v1/dev/reset-demo-access?fulfillmentMode=${mode}`, {
      headers: { Origin: webUrl() },
    });
    expect(response.status()).toBe(204);
  } finally {
    await context.close();
  }
}
