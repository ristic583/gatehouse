import { expect, type Page } from "@playwright/test";
import { demoPassword } from "./demo-users";

/**
 * Signs in through the Gatehouse email screen and the tenant's upstream IdP
 * (a Keycloak login form for both the OIDC and the SAML upstream).
 */
export async function signIn(page: Page, email: string): Promise<void> {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login/, { timeout: 30_000 });
  await page.getByLabel("Work email").fill(email);
  await page.getByRole("button", { name: "Continue with email" }).click();
  await page.waitForURL(/\/realms\//, { timeout: 30_000 });
  // Older Keycloak versions prefill the broker screen instead of consuming login_hint automatically.
  if (new URL(page.url()).pathname.startsWith("/realms/gatehouse/")) {
    await page.locator("#username").fill(email);
    await page.locator("#kc-login").click();
  }

  await expect(page).toHaveURL(/localhost:8081\/realms\/(nordlys-oidc|bricks-saml)\//);
  await page.locator("#username").fill(email.split("@")[0] ?? email);
  await page.locator("#password").fill(demoPassword());
  await page.locator("#kc-login").click();

  await page.waitForURL(new RegExp(`^${escapeRegExp(webUrl())}/(map|approvals|dashboard)?(\\?.*)?$`));
}

export function webUrl(): string {
  return (process.env.GATEHOUSE_WEB_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
