import { expect, test } from "@playwright/test";
import { openFloor } from "../fixtures/map";
import { expectTask, openQueueItem, pageFor, requestAndApproveServerRoom } from "../fixtures/flows";
import { resetDemoAccess } from "../fixtures/reset";

const simulatorUrl = process.env.LEGACYPACS_SIM_URL ?? "http://localhost:5180";

test.setTimeout(150_000);

test.beforeEach(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
});

test("approval is applied by the agent without staff", async ({ browser }) => {
  const anna = await requestAndApproveServerRoom(browser);
  await expect(async () => {
    await openFloor(anna, "Second floor");
    await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
  }).toPass({ timeout: 60_000 });

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openQueueItem(jonas, "Anna Larsen", "Server Room", undefined, "done");
  await expect(jonas.getByTestId("connector-status")).toBeVisible();
  await expect(jonas.getByRole("button", { name: "Confirm", exact: true })).toHaveCount(0);
  await expect(jonas.getByText(/Confirmed by/)).toBeVisible();
  await anna.context().close();
  await jonas.context().close();
});

test("permanent PACS failure fails the fulfillment and opens a task", async ({ browser, request }) => {
  const chaos = await request.post(`${simulatorUrl}/chaos`, { data: { failNext: 0, failNextPermanent: 2, delayMs: 0 } });
  expect(chaos.ok()).toBe(true);
  try {
    const anna = await requestAndApproveServerRoom(browser);
    const jonas = await pageFor(browser, "jonas.nielsen");
    await expect(async () => {
      await openQueueItem(jonas, "Anna Larsen", "Server Room");
      await expect(jonas.getByTestId("fulfillment-detail").getByText("Failed", { exact: true }).first()).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 60_000 });
    await expect(jonas.getByTestId("connector-status")).toContainText("Command status:");
    await expect(jonas.getByRole("button", { name: "Retry", exact: true })).toBeVisible();

    await expectTask(jonas, "Grant failed: Anna Larsen → Server Room", true);
    await anna.context().close();
    await jonas.context().close();
  } finally {
    await request.delete(`${simulatorUrl}/chaos`);
  }
});
