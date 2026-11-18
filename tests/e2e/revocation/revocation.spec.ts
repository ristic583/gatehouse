import { expect, test } from "@playwright/test";
import { areaState, openQueueItem, ownerRevokes, pageFor } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";
import { webUrl } from "../fixtures/sign-in";

test.beforeEach(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

test("owner revoke stays pending until confirmed", async ({ browser }) => {
  const erik = await pageFor(browser, "erik.moller");
  await ownerRevokes(erik, "Second floor", "CPH-OFFICE-2", "Anna Larsen");

  const anna = await pageFor(browser, "anna.larsen");
  expect(await areaState(anna, "Second floor", "CPH-OFFICE-2")).toBe("Pending");

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openQueueItem(jonas, "Anna Larsen", "Office Second", "Revoke");
  await jonas.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(jonas.getByText(/Confirmed by Jonas Nielsen/)).toBeVisible();

  expect(await areaState(anna, "Second floor", "CPH-OFFICE-2")).toBe("NoAccess");
  for (const p of [erik, anna, jonas]) {
    await p.context().close();
  }
});

test("non-owner cannot revoke", async ({ browser }) => {
  const anna = await pageFor(browser, "anna.larsen");
  const list = await anna.request.get("/api/proxy/v1/access-requests");
  const requests = (await list.json()) as { entitlementId: string; area: { name: string } }[];
  const id = requests.find((r) => r.area.name === "Office Second")?.entitlementId;
  expect(id).toBeDefined();
  await anna.context().close();

  const mikkel = await pageFor(browser, "mikkel.holm");
  const response = await mikkel.request.post(`/api/proxy/v1/access-requests/${id}/revoke`, {
    data: { reason: "Not allowed" },
    headers: { Origin: webUrl() },
  });
  expect(response.status()).toBe(403);
  await mikkel.context().close();
});

test("emergency revoke is first in the queue", async ({ browser }) => {
  const erik = await pageFor(browser, "erik.moller");
  await ownerRevokes(erik, "First floor", "CPH-OFFICE-1", "Anna Larsen");

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openFloor(jonas, "Ground floor");
  await jonas.getByTestId("area-CPH-LOBBY").click();
  await jonas.locator(".gh-area-detail").getByRole("link", { name: "View roster" }).click();
  const row = jonas.locator("[data-testid^='roster-']").filter({ hasText: "Anna Larsen" });
  await row.getByRole("button", { name: "Emergency revoke" }).click();
  await jonas.getByLabel("Reason").fill("Lost badge reported");
  await jonas.getByRole("button", { name: "Emergency revoke" }).last().click();
  await expect(row.getByText("Revocation pending")).toBeVisible();

  await jonas.goto("/console?tab=fulfillment");
  const first = jonas.locator("[data-testid^='fulfillment-']").first();
  await expect(first.getByText("Emergency")).toBeVisible();
  await expect(first).toContainText("Lobby");
  await erik.context().close();
  await jonas.context().close();
});

test("requester cancels a pending request", async ({ browser }) => {
  const anna = await pageFor(browser, "anna.larsen");
  await anna.goto("/requests");
  const row = anna.locator("[data-testid^='request-']").filter({ hasText: "Lab" });
  await row.getByRole("button", { name: "Cancel request" }).click();
  await anna.getByRole("button", { name: "Confirm cancel" }).click();
  await expect(row.getByText("Cancelled")).toBeVisible();
  expect(await areaState(anna, "First floor", "CPH-LAB")).toBe("NoAccess");
  await anna.context().close();
});
