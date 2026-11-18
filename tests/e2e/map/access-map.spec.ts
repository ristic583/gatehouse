import { expect, test } from "@playwright/test";
import { storageStateFor } from "../fixtures/auth-state";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";

test.use({ storageState: storageStateFor("anna.larsen") });

test.beforeEach(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

test("shows all four states with label and icon", async ({ page }) => {
  await openFloor(page, "Second floor");

  const expected = [
    ["CPH-OFFICE-2", "HasAccess", "Has access"],
    ["CPH-PLANT", "Unknown", "Unknown"],
    ["CPH-SERVER", "NoAccess", "No access"],
    ["CPH-VAULT", "Pending", "Pending"],
  ] as const;
  for (const [code, state, label] of expected) {
    const area = page.getByTestId(`area-${code}`);
    await expect(area).toHaveAttribute("data-state", state);
    await expect(area).toContainText(label);
    await expect(area.locator("svg")).toHaveCount(1);
  }
});

test("inspecting an area grants nothing and requesting is an explicit action", async ({ page }) => {
  await openFloor(page, "Second floor");

  await page.getByTestId("area-CPH-SERVER").click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await page.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("heading", { name: /Server Room/ })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();

  await page.reload();
  await expect(page.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "NoAccess");
});
