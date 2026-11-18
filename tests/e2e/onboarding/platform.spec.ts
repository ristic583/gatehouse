import { expect, test } from "@playwright/test";
import { createTenant, newTenant, signInPlatform, waitForTenantStatus } from "../fixtures/platform";
import { demoUsers } from "../fixtures/demo-users";
import { signIn } from "../fixtures/sign-in";
import { acceptInvite, adminPassword, signInLocal } from "./steps";

test.describe("platform operator", () => {
  test("operator creates a tenant and sees it become active", async ({ page }) => {
    test.setTimeout(180_000);
    const tenant = newTenant();
    await signInPlatform(page);
    await expect(page.getByTestId("platform-badge-header")).toHaveText(/Platform/);

    await page.getByRole("link", { name: "New tenant" }).first().click();
    await page.getByLabel("Tenant id").fill(tenant.tenantId);
    await page.getByLabel("Display name").fill(tenant.displayName);
    await page.getByLabel("Email domain").fill(tenant.emailDomain);
    await page.getByLabel("Time zone (IANA)").fill("Europe/Copenhagen");
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await page.getByLabel("Admin email").fill(tenant.adminEmail);
    await page.getByLabel("Given name").fill(tenant.adminGivenName);
    await page.getByLabel("Family name").fill(tenant.adminFamilyName);
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await expect(page.getByTestId("create-tenant-review")).toContainText(tenant.adminEmail);
    await page.getByRole("button", { name: "Create tenant" }).click();

    await expect(page.getByTestId("provisioning-progress")).toBeVisible();
    await expect(page.getByTestId("provisioning-done")).toBeVisible({ timeout: 120_000 });
    await expect(page.getByTestId("provisioning-step-AdminInvite")).toHaveAttribute("data-state", "done");

    await page.goto("/platform");
    const row = page.locator(`[data-testid="platform-tenant-row"][data-tenant-id="${tenant.tenantId}"]`);
    await expect(row).toBeVisible();
    await expect(row.getByTestId("tenant-status")).toHaveText("Active");
  });

  test("suspended tenant admin is refused and reactivation restores access", async ({ page, browser }) => {
    test.setTimeout(240_000);
    await signInPlatform(page);
    const tenant = await createTenant(page.request);
    const admin = await (await browser.newContext()).newPage();
    const password = adminPassword();
    await acceptInvite(admin, tenant.adminEmail, password);
    await signInLocal(admin, tenant.adminEmail, password);
    expect((await admin.request.get("/api/proxy/v1/me")).status()).toBe(200);

    await page.goto(`/platform/tenants/${tenant.tenantId}`);
    await page.getByRole("button", { name: "Suspend…" }).click();
    await page.getByLabel("Reason").fill("E2E: unpaid invoice");
    await page.getByRole("dialog").getByRole("button", { name: "Suspend" }).click();
    await expect(page.getByTestId("tenant-suspended-reason")).toContainText("E2E: unpaid invoice");

    await expect.poll(async () => (await admin.request.get("/api/proxy/v1/me")).status(), { timeout: 30_000 }).toBeGreaterThanOrEqual(401);
    expect([401, 403]).toContain((await admin.request.get("/api/proxy/v1/me")).status());

    await page.getByRole("button", { name: "Reactivate" }).click();
    await waitForTenantStatus(page.request, tenant.tenantId, "Active", 30_000);
    await expect.poll(async () => (await admin.request.get("/api/proxy/v1/me")).status(), { timeout: 30_000 }).toBe(200);
    await admin.context().close();
  });

  test("operator session survives a tenant sign-in in the same browser", async ({ page }) => {
    test.setTimeout(120_000);
    await signInPlatform(page);
    await expect(page.getByTestId("platform-badge-header")).toHaveText(/Platform/);

    // Same browser context: the tenant sign-in must not end or replace the operator session (lab #18).
    await signIn(page, demoUsers.annaLarsen);
    expect((await page.request.get("/api/proxy/v1/me")).status()).toBe(200);

    await page.goto("/platform");
    await expect(page).toHaveURL(/\/platform$/);
    await expect(page.getByTestId("platform-shell")).toBeVisible();
    await expect(page.getByTestId("platform-badge-header")).toHaveText(/Platform/);
    // The shell is chosen by route: no tenant sidebar on /platform even with a tenant cookie (lab #23).
    await expect(page.getByTestId("current-tenant")).toHaveCount(0);
    expect((await page.request.get("/api/proxy/v1/platform/tenants")).status()).toBe(200);

    // Tenant logout keeps the operator signed in.
    await page.goto("/map");
    await page.getByRole("button", { name: "Sign out" }).click();
    await page.waitForURL((url) => !url.pathname.startsWith("/map"));
    expect((await page.request.get("/api/proxy/v1/me")).status()).toBe(401);
    expect((await page.request.get("/api/proxy/v1/platform/tenants")).status()).toBe(200);
  });
});
