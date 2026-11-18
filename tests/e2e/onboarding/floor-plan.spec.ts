import { expect, test } from "@playwright/test";
import { drawAreaWithDoor, siteWithFloor, tenantWithAdmin, TINY_PNG } from "./steps";

test("admin uploads a drawing, draws a site, floor, area and door that appear on the map and in 3D", async ({ browser }) => {
  test.setTimeout(240_000);
  const { tenant, admin } = await tenantWithAdmin(browser);
  const { siteName, floorName } = await siteWithFloor(admin, "HQ");

  await admin.getByTestId("floor-background").setInputFiles({ name: "plan.png", mimeType: "image/png", buffer: TINY_PNG });
  await expect(admin.getByTestId("editor-background")).toBeVisible({ timeout: 30_000 });

  await drawAreaWithDoor(admin, { name: "Lab", code: "HQ-LAB", level: "Medium", ownerQuery: "Ada", ownerName: `Ada ${tenant.adminFamilyName}` });
  await expect(admin.getByTestId("editor-area-HQ-LAB")).toBeVisible();
  await expect(admin.getByTestId("editor-door-Lab door")).toBeVisible();

  await admin.getByRole("button", { name: "Preview 3D" }).click();
  await expect(admin.getByTestId("plan-preview")).toBeVisible();
  await expect(admin.getByTestId("plan-preview")).toContainText("Lab");

  await admin.goto("/map");
  const site = admin.getByRole("combobox", { name: "Site" });
  if (await site.isVisible().catch(() => false)) await site.selectOption({ label: siteName });
  await admin.getByRole("combobox", { name: "Floor" }).selectOption({ label: floorName });
  await admin.getByRole("button", { name: "Floor plan", exact: true }).click();
  await expect(admin.getByTestId("floor-map")).toBeVisible();
  await expect(admin.getByTestId("area-HQ-LAB")).toBeVisible();
  await expect(admin.getByTestId("plan-underlay")).toBeVisible();
  await admin.context().close();
});
