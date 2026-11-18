import { expect, test, type Page } from "@playwright/test";
import { acknowledgeOpenAlarms, clearScenarios, scenario, swipe, waitForAlarm } from "../fixtures/building";
import { pageFor, requestAndApproveServerRoom } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";

test.setTimeout(150_000);

async function openLive(page: Page, floorName: string): Promise<void> {
  await page.goto("/live");
  await page.getByRole("combobox", { name: "Site" }).selectOption({ label: "HQ Copenhagen" });
  await page.getByRole("combobox", { name: "Floor" }).selectOption({ label: floorName });
  await expect(page.getByTestId("live-floor-plan")).toBeVisible();
  await expect(page.getByTestId("live-status")).toHaveAttribute("data-status", "connected", { timeout: 15_000 });
}

function clock(ms: number): string {
  const d = new Date(ms);
  return [d.getHours(), d.getMinutes(), d.getSeconds()].map((n) => String(n).padStart(2, "0")).join(":");
}

function alarmRows(page: Page, doorName: string, kind: "Forced" | "HeldOpen") {
  return page.locator(`[data-testid='alarm-row'][data-kind='${kind}']`).filter({ hasText: doorName });
}

async function acknowledgeNewest(page: Page, doorName: string, kind: "Forced" | "HeldOpen", comment: string): Promise<void> {
  const row = alarmRows(page, doorName, kind).first();
  await row.getByRole("button", { name: "Acknowledge" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Comment").fill(comment);
  await dialog.getByRole("button", { name: "Acknowledge" }).click();
  await expect(dialog).toBeHidden();
}

test("granted swipe appears within 2 s", async ({ browser, request }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const anna = await requestAndApproveServerRoom(browser);
  await expect(async () => {
    await openFloor(anna, "Second floor");
    await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
  await anna.context().close();

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openLive(jonas, "Second floor");

  const before = Date.now();
  const decision = await swipe(request, "D-CPH-SERVER", "Anna Larsen");
  const responded = Date.now();
  expect(decision.decision).toBe("Granted");

  const times = new Set([clock(before - 1_000), clock(before), clock(responded), clock(responded + 1_000)]);
  const rows = jonas.getByTestId("live-event").filter({ hasText: "Anna Larsen · Server Room · Granted" });
  await expect(async () => {
    const texts = await rows.allTextContents();
    expect(texts.some((t) => times.has(t.slice(0, 8)))).toBe(true);
  }).toPass({ timeout: 2_000, intervals: [100] });
  expect(Date.now() - responded).toBeLessThan(2_500);
  await jonas.context().close();
});

test("forced door raises alarm and acknowledge is audited", async ({ browser, request }) => {
  // Own data: the vault door is used by no other spec; earlier runs may have left scenarios or open alarms on it.
  const jonas = await pageFor(browser, "jonas.nielsen");
  await clearScenarios(request);
  await acknowledgeOpenAlarms(jonas, "Vault door", "E2E: closing alarms left by earlier runs");
  await openLive(jonas, "Second floor");

  const started = Date.now();
  await scenario(request, "forced-door", "D-CPH-VAULT");
  await expect(jonas.getByTestId("door-D-CPH-VAULT")).toHaveAttribute("data-state", "ForcedOpen", { timeout: 10_000 });
  const raised = await waitForAlarm(jonas, "Vault door", "Forced", started);
  const row = alarmRows(jonas, "Vault door", "Forced").filter({ hasText: clock(Date.parse(raised.raisedAt)) });
  await expect(row).toBeVisible({ timeout: 10_000 });

  await row.getByRole("button", { name: "Acknowledge" }).click();
  const dialog = jonas.getByRole("dialog");
  await dialog.getByLabel("Comment").fill("Checked on camera, contractor");
  await dialog.getByRole("button", { name: "Acknowledge" }).click();
  await expect(dialog).toBeHidden();
  await expect(row).toHaveCount(0, { timeout: 10_000 });
  await jonas.context().close();

  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto("/console?tab=audit");
  const response = await freja.request.get("/api/proxy/v1/audit/entries?limit=50");
  expect(response.ok()).toBe(true);
  const entries = (await response.json()) as Array<{ action: string; subjectId: string }>;
  expect(entries.some((e) => e.action === "alarm.acknowledged" && e.subjectId === raised.alarmId)).toBe(true);
  await freja.context().close();
});

test("held open raises alarm", async ({ browser, request }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openLive(jonas, "Second floor");
  const alarms = alarmRows(jonas, "Plant Room door", "HeldOpen");
  const before = await alarms.count();

  await scenario(request, "held-open", "D-CPH-PLANT");
  await expect(alarms).toHaveCount(before + 1, { timeout: 15_000 });
  await expect(alarms.first()).toHaveAttribute("data-kind", "HeldOpen");

  await acknowledgeNewest(jonas, "Plant Room door", "HeldOpen", "Delivery, door closed again");
  await expect(alarms).toHaveCount(before, { timeout: 10_000 });
  await jonas.context().close();
});

test("lockdown reaches all floor doors", async ({ browser }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openLive(jonas, "Second floor");
  const doors = jonas.getByTestId("live-floor-plan").locator("[data-testid^='door-D-']");
  await expect(doors.first()).toBeVisible();
  const count = await doors.count();
  expect(count).toBeGreaterThan(1);

  const send = async (button: "Lockdown" | "Clear lockdown", confirm: string) => {
    await jonas.getByRole("button", { name: button, exact: true }).click();
    const dialog = jonas.getByRole("dialog");
    await dialog.getByLabel("Reason").fill("E2E lockdown drill");
    await dialog.getByRole("button", { name: "Continue" }).click();
    await dialog.getByRole("button", { name: confirm }).click();
    await expect(dialog.getByTestId("lockdown-result")).toBeVisible();
    await dialog.getByRole("button", { name: "Done" }).click();
    await expect(dialog).toBeHidden();
  };

  await send("Lockdown", "Confirm lockdown");
  try {
    await expect(jonas.getByTestId("live-floor-plan").locator("[data-testid^='door-D-'][data-state='Lockdown']")).toHaveCount(count, {
      timeout: 30_000,
    });
  } finally {
    await send("Clear lockdown", "Confirm clear");
  }
  await expect(jonas.getByTestId("live-floor-plan").locator("[data-testid^='door-D-'][data-state='Lockdown']")).toHaveCount(0, {
    timeout: 30_000,
  });
  await jonas.context().close();
});

test("connector health shows HQ-Copenhagen online", async ({ browser }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openLive(jonas, "Ground floor");
  const health = jonas.getByTestId("connector-health").filter({ hasText: "HQ-Copenhagen" });
  await expect(health).toHaveAttribute("data-online", "true", { timeout: 30_000 });
  await expect(health.getByTestId("connector-badge")).toHaveText("Online");
  await jonas.context().close();
});
