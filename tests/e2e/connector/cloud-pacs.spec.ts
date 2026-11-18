import { expect, test, type Page } from "@playwright/test";
import { swipe } from "../fixtures/building";
import { cloudPacsAssignments } from "../fixtures/cloudpacs";
import { openQueueItem, pageFor, requestAndApprove } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";

const SITE = "Warehouse Aarhus";
const FLOOR = "Warehouse floor";

test.setTimeout(150_000);

async function openAarhusLive(page: Page): Promise<void> {
  await page.goto("/live");
  await page.getByRole("combobox", { name: "Site" }).selectOption({ label: SITE });
  await page.getByRole("combobox", { name: "Floor" }).selectOption({ label: FLOOR });
  await expect(page.getByTestId("live-floor-plan")).toBeVisible();
  await expect(page.getByTestId("live-status")).toHaveAttribute("data-status", "connected", { timeout: 15_000 });
}

function clock(ms: number): string {
  const d = new Date(ms);
  return [d.getHours(), d.getMinutes(), d.getSeconds()].map((n) => String(n).padStart(2, "0")).join(":");
}

test("approval in Warehouse Aarhus is applied by the CloudPACS agent", async ({ browser, request }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const anna = await requestAndApprove(browser, {
    site: SITE,
    floor: FLOOR,
    code: "AAR-DOCK",
    areaName: "Loading Dock",
    approvers: ["erik.moller"],
    justification: "Inbound pallet checks for the winter season",
  });
  await expect(async () => {
    await openFloor(anna, FLOOR, SITE);
    await expect(anna.getByTestId("area-AAR-DOCK")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
  await anna.context().close();

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openQueueItem(jonas, "Anna Larsen", "Loading Dock", undefined, "done");
  await expect(jonas.getByTestId("connector-status")).toBeVisible();
  await expect(jonas.getByText(/Confirmed by/)).toBeVisible();
  await expect(jonas.getByTestId("capability-gaps")).toContainText("End date is enforced by the agent.");
  await jonas.context().close();

  const assignments = await cloudPacsAssignments(request);
  expect(assignments.some((a) => a.groupId === "AG-AAR-DOCK" && a.active && a.holderName === "Anna Larsen")).toBe(true);
});

test("health strip shows CloudPACS capability gaps", async ({ browser }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openAarhusLive(jonas);
  const aarhus = jonas.getByTestId("connector-health").filter({ hasText: "Warehouse-Aarhus" });
  await expect(aarhus).toHaveAttribute("data-online", "true", { timeout: 30_000 });
  await expect(aarhus.locator("[data-testid='capability-gap'][data-gap='NativeValidity']")).toBeVisible();
  await expect(aarhus.locator("[data-testid='capability-gap'][data-gap='Lockdown']")).toBeVisible();
  await jonas.context().close();
});

test("swipe at a CloudPACS door appears within 2 s", async ({ browser, request }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openAarhusLive(jonas);

  const before = Date.now();
  const decision = await swipe(request, "D-AAR-DOCK");
  const responded = Date.now();
  expect(decision.decision).toBe("Denied");

  const times = new Set([clock(before - 1_000), clock(before), clock(responded), clock(responded + 1_000)]);
  const rows = jonas.getByTestId("live-event").filter({ hasText: "Loading Dock" });
  await expect(async () => {
    const texts = await rows.allTextContents();
    expect(texts.some((t) => times.has(t.slice(0, 8)))).toBe(true);
  }).toPass({ timeout: 2_000, intervals: [100] });
  expect(Date.now() - responded).toBeLessThan(2_500);
  await jonas.context().close();
});

test("lockdown is refused for CloudPACS-only floor", async ({ browser }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openAarhusLive(jonas);
  await jonas.getByRole("button", { name: "Lockdown", exact: true }).click();
  const dialog = jonas.getByRole("dialog");
  await dialog.getByLabel("Reason").fill("E2E lockdown drill");
  await dialog.getByRole("button", { name: "Continue" }).click();
  await dialog.getByRole("button", { name: "Confirm lockdown" }).click();
  await expect(dialog.getByTestId("lockdown-unsupported")).toBeVisible();
  await expect(dialog).toBeVisible();
  await jonas.context().close();
});
