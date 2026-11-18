import { expect, test, type Page } from "@playwright/test";
import { cloudPacsApi, cloudPacsAssignments, cloudPacsSwipe } from "../fixtures/cloudpacs";
import { pageFor, requestAndApprove } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { personIdOf, resolveTask, runReconciliation } from "../fixtures/reconciliation";
import { resetDemoAccess } from "../fixtures/reset";

const SITE = "Warehouse Aarhus";
const FLOOR = "Warehouse floor";
const INSTANCE = "Warehouse-Aarhus";
const GROUP = "AG-AAR-DOCK";
const DOOR = "D-AAR-DOCK";
const ROGUE_CARD = "7700880099001122";

test.setTimeout(180_000);

/** Adds mikkel (no Aarhus entitlement) to the dock group directly in CloudPACS; returns his person id. */
async function addRogueMember(page: Page, rogueId: string): Promise<void> {
  const api = cloudPacsApi(page.request);
  await api.upsertCardholder(rogueId, "Mikkel", "Holm", ROGUE_CARD);
  await api.addMember(GROUP, rogueId, ROGUE_CARD);
}

async function openAarhusLive(page: Page): Promise<void> {
  await page.goto("/live");
  await page.getByRole("combobox", { name: "Site" }).selectOption({ label: SITE });
  await page.getByRole("combobox", { name: "Floor" }).selectOption({ label: FLOOR });
  await expect(page.getByTestId("live-status")).toHaveAttribute("data-status", "connected", { timeout: 15_000 });
}

function driftAlarms(page: Page) {
  return page.locator("[data-testid='alarm-row'][data-kind='UnauthorizedAccess']").filter({ hasText: "Loading Dock" });
}

async function acknowledgeDriftAlarms(page: Page): Promise<void> {
  await openAarhusLive(page);
  const rows = driftAlarms(page);
  while ((await rows.count()) > 0) {
    await rows.first().getByRole("button", { name: "Acknowledge" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Comment").fill("E2E drift drill");
    await dialog.getByRole("button", { name: "Acknowledge" }).click();
    await expect(dialog).toBeHidden();
  }
}

function taskIdFrom(href: string | null): string {
  const id = new URL(href ?? "", "http://x").searchParams.get("item");
  expect(id).not.toBeNull();
  return id!;
}

test("missing and extra findings for Warehouse Aarhus", async ({ browser, request }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const rogueId = await personIdOf(browser, "mikkel.holm");
  const api = cloudPacsApi(request);
  try {
    const anna = await requestAndApprove(browser, {
      site: SITE,
      floor: FLOOR,
      code: "AAR-DOCK",
      areaName: "Loading Dock",
      approvers: ["erik.moller"],
      justification: "Inbound pallet checks for the reconciliation drill",
    });
    await expect(async () => {
      await openFloor(anna, FLOOR, SITE);
      await expect(anna.getByTestId("area-AAR-DOCK")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
    }).toPass({ timeout: 60_000 });
    await anna.context().close();
    const annaRef = (await cloudPacsAssignments(request)).find((a) => a.groupId === GROUP && a.active && a.holderName === "Anna Larsen")!.externalRef;

    await api.removeMember(GROUP, annaRef);
    const jonas = await pageFor(browser, "jonas.nielsen");
    await addRogueMember(jonas, rogueId);

    const report = await runReconciliation(jonas, INSTANCE);
    await expect(report.getByTestId("recon-captured-at")).not.toHaveText("–");
    await expect(report.getByTestId("recon-compared-at")).not.toHaveText("–");
    await expect(report.getByTestId("recon-coverage")).toContainText("Loading Dock");
    await expect(report.getByTestId("recon-in-sync")).toBeVisible();
    const missing = report.locator("[data-testid='recon-finding'][data-kind='MissingInPacs']").filter({ hasText: "Anna Larsen" });
    const extra = report.locator("[data-testid='recon-finding'][data-kind='ExtraInPacs']").filter({ hasText: "Mikkel Holm" });
    await expect(missing).toContainText("Missing in PACS");
    await expect(extra).toContainText("Extra in PACS");
    const missingTask = taskIdFrom(await missing.getByRole("link", { name: "Open task" }).getAttribute("href"));
    const extraTask = taskIdFrom(await extra.getByRole("link", { name: "Open task" }).getAttribute("href"));

    await resolveTask(jonas, missingTask, "Re-push to PACS");
    await expect(async () => {
      const assignments = await cloudPacsAssignments(request);
      expect(assignments.some((a) => a.groupId === GROUP && a.active && a.externalRef === annaRef)).toBe(true);
    }).toPass({ timeout: 60_000 });

    await resolveTask(jonas, extraTask, "Remove from PACS");
    await expect(async () => {
      const assignments = await cloudPacsAssignments(request);
      expect(assignments.some((a) => a.groupId === GROUP && a.active && a.externalRef === rogueId)).toBe(false);
    }).toPass({ timeout: 60_000 });
    await jonas.context().close();
  } finally {
    await api.removeMember(GROUP, rogueId);
  }
});

test("granted swipe without entitlement raises a live drift alarm", async ({ browser, request }) => {
  const rogueId = await personIdOf(browser, "mikkel.holm");
  const jonas = await pageFor(browser, "jonas.nielsen");
  try {
    await acknowledgeDriftAlarms(jonas);
    await addRogueMember(jonas, rogueId);

    expect(await cloudPacsSwipe(request, DOOR, rogueId)).toBe("granted");

    const alarm = driftAlarms(jonas).first();
    await expect(alarm).toBeVisible({ timeout: 5_000 });
    await expect(alarm).toContainText("Access without entitlement");
    await acknowledgeDriftAlarms(jonas);
  } finally {
    await cloudPacsApi(request).removeMember(GROUP, rogueId);
    await jonas.context().close();
  }
});

test("drift task cannot be closed without an action", async ({ browser, request }) => {
  const rogueId = await personIdOf(browser, "mikkel.holm");
  const jonas = await pageFor(browser, "jonas.nielsen");
  try {
    await acknowledgeDriftAlarms(jonas);
    await addRogueMember(jonas, rogueId);
    expect(await cloudPacsSwipe(request, DOOR, rogueId)).toBe("granted");

    const task = jonas.locator("[data-testid^='task-']").filter({ hasText: "Drift alert" }).filter({ hasText: "Loading Dock" }).first();
    await expect(async () => {
      await jonas.goto("/console?tab=tasks");
      await expect(task).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    await task.getByRole("button", { name: "Resolve" }).click();
    const dialog = jonas.getByRole("dialog");
    const confirm = dialog.getByRole("button", { name: "Confirm resolve" });
    await expect(confirm).toBeDisabled();
    await dialog.getByLabel("Resolution").selectOption({ label: "Dismissed" });
    await expect(confirm).toBeDisabled();
    await dialog.getByLabel("Note (required)").fill("Mikkel was escorted by dock staff");
    await expect(confirm).toBeEnabled();
    await confirm.click();
    await expect(dialog).toBeHidden();
    await acknowledgeDriftAlarms(jonas);
  } finally {
    await cloudPacsApi(request).removeMember(GROUP, rogueId);
    await jonas.context().close();
  }
});

test("tenant admin sees reports read-only", async ({ browser }) => {
  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto("/console?tab=reconciliation");
  await expect(freja.getByTestId("recon-instance").filter({ hasText: INSTANCE })).toBeVisible();
  await expect(freja.getByTestId("recon-report-row").first()).toBeVisible();
  await expect(freja.getByRole("button", { name: "Run reconciliation" })).toHaveCount(0);
  await freja.context().close();
});
