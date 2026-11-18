import { expect, type Page } from "@playwright/test";

/** Opens the access map on a floor of a site (default: the first site). */
export async function openFloor(page: Page, floorName: string, siteName?: string): Promise<void> {
  await page.goto("/map");
  if (siteName !== undefined) {
    await page.getByRole("combobox", { name: "Site" }).selectOption({ label: siteName });
  }
  const floor = page.getByRole("combobox", { name: "Floor" });
  await expect(floor).toBeVisible();
  await floor.selectOption({ label: floorName });
  await page.getByRole("button", { name: "Floor plan", exact: true }).click();
  await expect(page.getByTestId("floor-map")).toBeVisible();
}
