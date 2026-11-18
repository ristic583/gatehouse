import { expect, type Browser, type Page } from "@playwright/test";
import { demoPassword } from "../fixtures/demo-users";
import { actionLinkFor } from "../fixtures/mailpit";
import { createTenant, type NewTenant, newTenant, signInPlatform } from "../fixtures/platform";
import { webUrl } from "../fixtures/sign-in";
import { ACME_CLIENT_ID, acmeClientSecret, acmeDiscoveryUrl } from "../fixtures/upstream-idp";

const escape = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
export const backToWeb = () => new RegExp(`^${escape(webUrl())}/`);
/** Where `/` finally sends a signed-in user; waiting for it avoids racing the post-login redirect with a goto. */
export const landed = () => new RegExp(`^${escape(webUrl())}/(map|approvals|dashboard|onboarding)(\\?.*)?$`);
export const adminPassword = () => `E2e-${Date.now()}-Pw!`;

/** Opens the Keycloak invitation link from Mailpit and sets the admin's password (UPDATE_PASSWORD action). */
export async function acceptInvite(page: Page, email: string, password: string): Promise<void> {
  await page.goto(await actionLinkFor(email));
  const proceed = page.getByRole("link", { name: /click here to proceed/i });
  if (await proceed.isVisible().catch(() => false)) await proceed.click();
  await page.locator("#password-new").fill(password);
  await page.locator("#password-confirm").fill(password);
  await page.locator("[type=submit]").first().click();
  await expect(page.locator("body")).toContainText(/account has been updated|updated/i);
}

/** Tenant user without a customer IdP: Gatehouse email screen → realm `gatehouse` username/password. */
export async function signInLocal(page: Page, email: string, password: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Work email").fill(email);
  await page.getByRole("button", { name: "Continue with email" }).click();
  await page.waitForURL(/\/realms\/gatehouse\//);
  if (!(await page.locator("#password").isVisible())) {
    await page.locator("#username").fill(email);
    await page.locator("#kc-login").click();
  }
  await page.locator("#password").fill(password);
  await page.locator("#kc-login").click();
  await page.waitForURL(landed());
}

/** Tenant user whose domain is routed to the acme OIDC IdP (upstream realm `acme-oidc`, username = email). */
export async function signInUpstream(page: Page, email: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Work email").fill(email);
  await page.getByRole("button", { name: "Continue with email" }).click();
  await page.waitForURL(/\/realms\//);
  if (new URL(page.url()).pathname.startsWith("/realms/gatehouse/")) {
    await page.locator("#username").fill(email);
    await page.locator("#kc-login").click();
  }
  await page.waitForURL(/localhost:8081\/realms\/acme-oidc\//);
  await page.locator("#username").fill(email);
  await page.locator("#password").fill(demoPassword());
  await page.locator("#kc-login").click();
  await page.waitForURL(landed());
}

export interface TenantWithAdmin {
  tenant: NewTenant;
  operator: Page;
  admin: Page;
  password: string;
}

/** Operator creates a fresh tenant; the invited admin sets a password and signs in (each in its own context). */
export async function tenantWithAdmin(browser: Browser): Promise<TenantWithAdmin> {
  const operator = await (await browser.newContext()).newPage();
  await signInPlatform(operator);
  const tenant = await createTenant(operator.request, newTenant());
  const admin = await (await browser.newContext()).newPage();
  const password = adminPassword();
  await acceptInvite(admin, tenant.adminEmail, password);
  await signInLocal(admin, tenant.adminEmail, password);
  return { tenant, operator, admin, password };
}

export async function addEmployee(page: Page, email: string, given: string, family: string): Promise<void> {
  await page.goto("/admin/people");
  await page.getByRole("button", { name: "Add employee" }).click();
  const dialog = page.getByTestId("add-employee-dialog");
  await dialog.getByLabel("Work email").fill(email);
  await dialog.getByLabel("Given name").fill(given);
  await dialog.getByLabel("Family name").fill(family);
  await dialog.getByRole("button", { name: "Add employee" }).click();
  await expect(dialog).toBeHidden();
}

export async function connectAcmeIdp(page: Page): Promise<void> {
  await page.goto("/admin/identity");
  await page.getByRole("radio", { name: "OIDC" }).click();
  await page.getByLabel("Display name").fill("Acme OIDC");
  await page.getByLabel("Discovery URL").fill(acmeDiscoveryUrl());
  await page.getByLabel("Client id").fill(ACME_CLIENT_ID);
  await page.getByLabel("Client secret").fill(acmeClientSecret());
  await page.getByRole("button", { name: "Test connection" }).click();
  await expect(page.getByTestId("idp-test-result")).toHaveAttribute("data-ok", "true", { timeout: 30_000 });
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByTestId("idp-broker-redirect-uri")).toBeVisible({ timeout: 30_000 });
}

/** Creates a site with one floor (1000×600) and opens the floor editor. Returns the site name. */
export async function siteWithFloor(page: Page, code: string): Promise<{ siteName: string; floorName: string }> {
  const siteName = `Site ${code}`;
  const floorName = `Floor ${code}`;
  await page.goto("/admin/sites");
  const form = page.getByRole("form", { name: "Create site" });
  await form.getByLabel("Site name").fill(siteName);
  await form.getByLabel("Site code").fill(code);
  await form.getByRole("button", { name: "Create site" }).click();
  await page.getByRole("link", { name: siteName }).click();
  const floorForm = page.getByRole("form", { name: "Add floor" });
  await floorForm.getByLabel("Floor name").fill(floorName);
  await floorForm.getByRole("button", { name: "Add floor" }).click();
  await page.getByRole("link", { name: floorName }).click();
  await expect(page.getByTestId("plan-editor-canvas")).toBeVisible();
  return { siteName, floorName };
}

/** Clicks plan coordinates on the editor canvas (plan 1000×600). */
export async function clickPlan(page: Page, x: number, y: number, plan = { width: 1000, height: 600 }): Promise<void> {
  const canvas = page.getByTestId("plan-editor-canvas");
  const box = await canvas.boundingBox();
  if (box === null) throw new Error("Editor canvas is not visible.");
  await canvas.click({ position: { x: (x * box.width) / plan.width, y: (y * box.height) / plan.height } });
}

/** Draws a rectangle area, names it, sets security level and owner, places a door on its top edge and saves. */
export async function drawAreaWithDoor(page: Page, area: { name: string; code: string; level: string; ownerQuery: string; ownerName: string; doorRef?: string }): Promise<void> {
  await page.getByRole("button", { name: "Draw area" }).click();
  for (const [x, y] of [[200, 150], [500, 150], [500, 400], [200, 400], [200, 150]] as const) await clickPlan(page, x, y);
  const inspector = page.getByTestId("area-inspector");
  await inspector.getByLabel("Area name").fill(area.name);
  await inspector.getByLabel("Area code").fill(area.code);
  await inspector.getByLabel("Security level").selectOption({ label: area.level });
  await inspector.getByLabel("Find owner").fill(area.ownerQuery);
  await inspector.getByRole("button", { name: "Find" }).click();
  await inspector.getByRole("button", { name: `Add ${area.ownerName}` }).click();
  await page.getByRole("button", { name: "Place door" }).click();
  await clickPlan(page, 350, 160);
  const door = page.getByTestId("door-inspector");
  await door.getByLabel("Door name").fill(`${area.name} door`);
  if (area.doorRef !== undefined) await door.getByLabel("PACS door reference").fill(area.doorRef);
  await page.getByRole("button", { name: "Save plan" }).click();
  // A clean save reloads the plan (the editor remounts); the toast confirms it.
  await expect(page.getByText("Plan saved")).toBeVisible({ timeout: 30_000 });
}

/** 1×1 PNG, enough for the background pipeline (decode + re-encode). */
export const TINY_PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
