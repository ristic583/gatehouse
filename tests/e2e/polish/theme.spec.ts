import { expect, test } from "@playwright/test";
import { apiAs } from "../fixtures/lifecycle";
import { pageFor } from "../fixtures/flows";
import { webUrl } from "../fixtures/sign-in";

const DEFAULT_THEME = { primary: "#171717", primaryForeground: "#fafafa", accent: "#f5f5f5", accentForeground: "#171717", background: "#ffffff", foreground: "#0a0a0a", radiusPx: 10, terms: {} };

test.afterAll(async ({ browser }) => {
  const freja = await apiAs(browser, "freja.dahl");
  const response = await freja.put(`${webUrl()}/api/proxy/v1/admin/theme`, { headers: { Origin: webUrl() }, data: DEFAULT_THEME });
  expect(response.ok()).toBe(true);
});

test("admin saves a passing theme; failing contrast is refused", async ({ browser }) => {
  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto("/console?tab=theme");
  await freja.getByLabel("Primary", { exact: true }).fill("#ffff00");
  await freja.getByLabel("Primary text", { exact: true }).fill("#ffffff");
  await expect(freja.getByTestId("contrast-primary")).toHaveAttribute("data-passes", "false");
  await expect(freja.getByRole("button", { name: "Save theme" })).toBeDisabled();

  await freja.getByLabel("Primary", { exact: true }).fill("#1e3a8a");
  await expect(freja.getByTestId("contrast-primary")).toHaveAttribute("data-passes", "true");
  await freja.getByRole("button", { name: "Save theme" }).click();
  await expect(freja.getByRole("status")).toHaveText(/Theme saved/);

  await freja.reload();
  await expect
    .poll(() => freja.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--gh-theme-primary").trim()))
    .toBe("#1e3a8a");
  const refused = await freja.request.put(`${webUrl()}/api/proxy/v1/admin/theme`, {
    headers: { Origin: webUrl() },
    data: { ...DEFAULT_THEME, primary: "#ffff00", primaryForeground: "#ffffff" },
  });
  expect(refused.status()).toBe(400);
  expect(await refused.text()).toContain("theme.contrast_failed");
  await freja.context().close();
});

test("bricks shows its own terminology", async ({ browser }) => {
  const lena = await pageFor(browser, "lena.schmidt");
  await lena.goto("/map");
  await lena.getByRole("button", { name: "Floor plan", exact: true }).click();
  await expect(lena.getByTestId("floor-map")).toBeVisible();
  await lena.locator("[data-testid^='area-'][data-state='NoAccess']").first().click();
  await lena.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  await expect(lena.getByRole("dialog").getByTestId("area-term")).toHaveText("Zone");
  await lena.context().close();
});
