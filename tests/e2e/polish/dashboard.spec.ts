import { expect, test } from "@playwright/test";
import { pageFor } from "../fixtures/flows";
import { resetDemoAccess } from "../fixtures/reset";

test.afterAll(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

test("dashboard counts link to the console", async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const jonas = await pageFor(browser, "jonas.nielsen");
  await jonas.goto("/dashboard");
  await expect(jonas.getByTestId("tile-tasks")).toBeVisible();
  await expect(jonas.getByTestId("dashboard-coverage")).toContainText("Warehouse-Aarhus");
  await jonas.getByTestId("tile-tasks").click();
  await expect(jonas).toHaveURL(/\/console\?tab=tasks/);
  const anna = await pageFor(browser, "anna.larsen");
  expect((await anna.request.get("/api/proxy/v1/dashboard")).status()).toBe(403);
  await Promise.all([jonas.context().close(), anna.context().close()]);
});
