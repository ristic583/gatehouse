import { expect, test } from "@playwright/test";
import { openFloor } from "../fixtures/map";
import { expectTask, openQueueItem, pageFor, serverRoomApproved } from "../fixtures/flows";
import { resetDemoAccess } from "../fixtures/reset";

test.beforeEach(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

test("staff confirmation turns the area green", async ({ browser }) => {
  const anna = await serverRoomApproved(browser);
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openQueueItem(jonas, "Anna Larsen", "Server Room");
  await expect(jonas.getByText("Not confirmed yet")).toBeVisible();
  await jonas.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(jonas.getByText(/Confirmed by Jonas Nielsen/)).toBeVisible();

  await openFloor(anna, "Second floor");
  await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "HasAccess");
  await anna.context().close();
  await jonas.context().close();
});

test("failure opens a task and retry recovers", async ({ browser }) => {
  const anna = await serverRoomApproved(browser);
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openQueueItem(jonas, "Anna Larsen", "Server Room");
  await jonas.getByRole("button", { name: "Fail", exact: true }).click();
  await jonas.getByLabel("Reason").fill("Controller offline");
  await jonas.getByRole("button", { name: "Confirm fail" }).click();
  await expect(jonas.getByText("Marked as failed")).toBeVisible();

  await expectTask(jonas, "Grant failed: Anna Larsen → Server Room", true);

  const freja = await pageFor(browser, "freja.dahl");
  await expectTask(freja, "Grant failed: Anna Larsen → Server Room", true);
  await expect(freja.getByRole("button", { name: "Resolve", exact: true })).toHaveCount(0);

  await openQueueItem(jonas, "Anna Larsen", "Server Room");
  await jonas.getByRole("button", { name: "Retry", exact: true }).click();
  await expect(jonas.getByText("Retry queued")).toBeVisible();
  await jonas.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(jonas.getByText(/Confirmed by Jonas Nielsen/)).toBeVisible();

  await expectTask(jonas, "Grant failed: Anna Larsen → Server Room", false);
  await openFloor(anna, "Second floor");
  await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "HasAccess");
  for (const p of [anna, jonas, freja]) {
    await p.context().close();
  }
});
