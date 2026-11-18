import { expect, type APIRequestContext, type Page } from "@playwright/test";
import { webUrl } from "./sign-in";
import { freshTotp } from "./totp";

function required(name: string, hint: string): string {
  const value = process.env[name];
  if (value === undefined || value === "") throw new Error(`${name} is not set. ${hint}`);
  return value;
}

export const operatorEmail = () => process.env.GATEHOUSE_PLATFORM_OPERATOR_EMAIL ?? "operator@platform.test";
const operatorPassword = () => required("GATEHOUSE_PLATFORM_OPERATOR_PASSWORD", "Read Parameters:platform-operator-password from the AppHost user secrets.");
const otpSecret = () => required("GATEHOUSE_PLATFORM_OTP_SECRET", "Read Parameters:platform-operator-otp-secret from the AppHost user secrets.");

/** Operator sign-in on realm `gatehouse-platform`: password, then TOTP. Ends on `/platform`. */
export async function signInPlatform(page: Page): Promise<void> {
  await page.goto("/platform/login");
  await page.getByRole("button", { name: "Sign in as operator" }).click();
  await page.waitForURL(/\/realms\/gatehouse-platform\//);
  await page.locator("#username").fill(operatorEmail());
  if (!(await page.locator("#password").isVisible())) await page.locator("#kc-login").click();
  await page.locator("#password").fill(operatorPassword());
  await page.locator("#kc-login").click();
  await page.locator("#otp").fill(await freshTotp(otpSecret()));
  await page.locator("#kc-login").click();
  await page.waitForURL(new RegExp(`^${webUrl().replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}/platform`));
}

export interface NewTenant {
  tenantId: string;
  displayName: string;
  emailDomain: string;
  adminEmail: string;
  adminGivenName: string;
  adminFamilyName: string;
}

/** Re-runnable ids: `e2e-<timestamp>` and domain `e<timestamp>.acme.test` (no cleanup; README "Leftover test tenants"). */
export function newTenant(): NewTenant {
  const stamp = `${Date.now()}${Math.floor(Math.random() * 90 + 10)}`;
  const domain = `e${stamp}.acme.test`;
  return {
    tenantId: `e2e-${stamp}`.slice(0, 32),
    displayName: `E2E ${stamp}`,
    emailDomain: domain,
    adminEmail: `admin@${domain}`,
    adminGivenName: "Ada",
    adminFamilyName: `Admin${stamp.slice(-4)}`,
  };
}

const sameOrigin = () => ({ Origin: webUrl() });

/** Creates a tenant through the BFF with the operator page's session and waits until it is Active. */
export async function createTenant(request: APIRequestContext, tenant: NewTenant = newTenant()): Promise<NewTenant> {
  const response = await request.post("/api/proxy/v1/platform/tenants", {
    headers: sameOrigin(),
    data: { ...tenant, timeZoneId: "Europe/Copenhagen" },
  });
  expect(response.status(), await response.text()).toBe(202);
  await waitForTenantStatus(request, tenant.tenantId, "Active");
  return tenant;
}

export async function waitForTenantStatus(request: APIRequestContext, tenantId: string, status: string, timeout = 120_000): Promise<void> {
  await expect
    .poll(async () => ((await (await request.get(`/api/proxy/v1/platform/tenants/${tenantId}`)).json()) as { status: string }).status, { timeout, intervals: [1000, 2000] })
    .toBe(status);
}
