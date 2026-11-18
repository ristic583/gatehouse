import { expect, type Browser, type Page } from "@playwright/test";
import { storageStateFor, type AuthUser } from "./auth-state";
import { openFloor } from "./map";

export async function pageFor(browser: Browser, user: AuthUser): Promise<Page> {
  const context = await browser.newContext({ storageState: storageStateFor(user) });
  return context.newPage();
}

export function inDays(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** Anna requests Server Room (14 days); mikkel then sofie approve. Returns anna's page (state Pending). */
export async function serverRoomApproved(browser: Browser): Promise<Page> {
  const anna = await requestAndApproveServerRoom(browser);
  await openFloor(anna, "Second floor");
  await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "Pending");
  return anna;
}

/** Anna requests Server Room (14 days); mikkel then sofie approve. Returns anna's page without asserting the map state. */
export async function requestAndApproveServerRoom(browser: Browser): Promise<Page> {
  return requestAndApprove(browser, {
    floor: "Second floor",
    code: "CPH-SERVER",
    areaName: "Server Room",
    approvers: ["mikkel.holm", "sofie.berg"],
    justification: "Rack migration for the new storage cluster",
  });
}

export interface AccessRequestFlow {
  site?: string;
  floor: string;
  code: string;
  areaName: string;
  approvers: readonly AuthUser[];
  justification: string;
}

/** Anna requests an area (14 days); the approvers approve in order. Returns anna's page without asserting the map state. */
export async function requestAndApprove(browser: Browser, flow: AccessRequestFlow): Promise<Page> {
  const anna = await pageFor(browser, "anna.larsen");
  await openFloor(anna, flow.floor, flow.site);
  await anna.getByTestId(`area-${flow.code}`).click();
  await anna.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  const dialog = anna.getByRole("dialog");
  await dialog.getByRole("button", { name: "Continue", exact: true }).click();
  await dialog.getByLabel("End date").fill(inDays(14));
  await dialog.getByLabel("Justification").fill(flow.justification);
  await dialog.getByRole("button", { name: "Review request" }).click();
  await dialog.getByRole("button", { name: "Send request" }).click();
  await expect(dialog).toBeHidden();
  for (const user of flow.approvers) {
    const page = await pageFor(browser, user);
    await page.goto("/approvals");
    const card = page.getByRole("article", { name: `Anna Larsen → ${flow.areaName}` });
    await card.getByRole("button", { name: "Approve" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Confirm approval" }).click();
    await expect(page.getByRole("dialog")).toBeHidden();
    await expect(card).toHaveCount(0);
    await page.context().close();
  }
  return anna;
}

/** Opens jonas' fulfillment queue (open or done view) and selects the row for a person and area. */
export async function openQueueItem(page: Page, person: string, area: string, kind?: string, view: "open" | "done" = "open"): Promise<void> {
  await page.goto("/console?tab=fulfillment");
  if (view === "done") {
    await page.getByRole("button", { name: "Done", exact: true }).click();
  }
  let row = page.locator("[data-testid^='fulfillment-']").filter({ hasText: person }).filter({ hasText: area });
  if (kind !== undefined) {
    row = row.filter({ hasText: kind });
  }
  await row.first().click();
  await expect(page.getByTestId("fulfillment-detail")).toBeVisible();
}

export async function areaState(page: Page, floor: string, code: string): Promise<string | null> {
  await openFloor(page, floor);
  return page.getByTestId(`area-${code}`).getAttribute("data-state");
}

/** Owner opens the roster of an area from the map dialog and revokes one person. */
export async function ownerRevokes(page: Page, floor: string, code: string, person: string): Promise<void> {
  await openFloor(page, floor);
  await page.getByTestId(`area-${code}`).click();
  await page.locator(".gh-area-detail").getByRole("link", { name: "View roster" }).click();
  const row = page.locator("[data-testid^='roster-']").filter({ hasText: person });
  await row.getByRole("button", { name: "Revoke", exact: true }).click();
  await page.getByLabel("Reason").fill("Role change, access no longer needed");
  await page.getByRole("button", { name: "Revoke access" }).click();
  await expect(row.getByText("Revocation pending")).toBeVisible();
}

/** Task follow-ups are opened and resolved by message consumers, so reload the task list until the expectation holds. */
export async function expectTask(page: Page, title: string, visible: boolean): Promise<void> {
  await expect(async () => {
    await page.goto("/console?tab=tasks");
    const task = page.getByText(title);
    if (visible) {
      await expect(task).toBeVisible({ timeout: 2_000 });
    } else {
      await expect(page.getByRole("button", { name: "Open", exact: true })).toBeVisible({ timeout: 2_000 });
      await expect(page.getByText("Loading…")).toHaveCount(0, { timeout: 2_000 });
      await expect(task).toHaveCount(0, { timeout: 2_000 });
    }
  }).toPass({ timeout: 30_000 });
}
