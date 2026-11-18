import path from "node:path";
import { expect, test, type Page } from "@playwright/test";

// Screenshots for the README (opt-in project "screenshots", see playwright.config.ts).
const out = (name: string) => path.join(__dirname, "..", "..", "..", "docs", "images", `${name}.jpg`);

async function shoot(page: Page, name: string): Promise<void> {
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(1500); // let map transitions and charts settle
  await page.screenshot({ path: out(name), type: "jpeg", quality: 82 });
}

async function signInAs(page: Page, name: string): Promise<void> {
  await page.goto("/login");
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/login") && !url.pathname.startsWith("/auth"), { timeout: 60_000 });
}

test.use({ storageState: { cookies: [], origins: [] } });

test("login", async ({ page }) => {
  await page.goto("/login");
  await expect(page.getByRole("button", { name: /Anna Larsen/ })).toBeVisible();
  await shoot(page, "login");
});

test("employee: map and requests", async ({ page }) => {
  await signInAs(page, "Anna Larsen");
  await page.goto("/map");
  await shoot(page, "map");
  await page.goto("/requests");
  await shoot(page, "requests");
});

test("manager: approvals", async ({ page }) => {
  await signInAs(page, "Mikkel Holm");
  await page.goto("/approvals");
  await shoot(page, "approvals");
});

test("security: live building, dashboard, how it works, audit", async ({ page }) => {
  await signInAs(page, "Jonas Nielsen");
  await page.goto("/live");
  await shoot(page, "live");
  await page.goto("/dashboard");
  await shoot(page, "dashboard");
  await page.goto("/how-it-works");
  await shoot(page, "how-it-works");
  await page.goto("/console?tab=audit");
  await shoot(page, "audit");
});

test("second tenant with its own theme", async ({ page }) => {
  await signInAs(page, "Lena Schmidt");
  await page.goto("/map");
  await shoot(page, "tenant-bricks");
});
