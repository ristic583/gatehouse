import { expect, type Browser, type Page } from "@playwright/test";
import { storageStateFor, type AuthUser } from "./auth-state";
import { webUrl } from "./sign-in";

/** The person id of a demo user (`GET /v1/me` through the BFF with that user's session). */
export async function personIdOf(browser: Browser, user: AuthUser): Promise<string> {
  const context = await browser.newContext({ storageState: storageStateFor(user) });
  try {
    const response = await context.request.get(`${webUrl()}/api/proxy/v1/me`);
    expect(response.ok(), `me: ${response.status()}`).toBe(true);
    const me = (await response.json()) as { person: { personId: string } | null };
    expect(me.person).not.toBeNull();
    return me.person!.personId;
  } finally {
    await context.close();
  }
}

/** Starts a reconciliation of a PACS instance from the console, waits for `Completed` and opens that report. */
export async function runReconciliation(page: Page, instanceName: string): Promise<Page> {
  await page.goto("/console?tab=reconciliation");
  const card = page.getByTestId("recon-instance").filter({ hasText: instanceName });
  const started = page.waitForResponse(
    (r) => r.request().method() === "POST" && r.url().endsWith("/v1/reconciliation/reports"),
  );
  await card.getByRole("button", { name: "Run reconciliation" }).click();
  const response = await started;
  expect(response.status(), "run reconciliation").toBe(202);
  const { reportId } = (await response.json()) as { reportId: string };
  await page.goto(`/console?tab=reconciliation&item=${reportId}`);
  await expect(page.getByTestId("recon-report")).toContainText("Completed", { timeout: 60_000 });
  await expect(page.getByTestId("recon-report-row").filter({ hasText: instanceName }).first()).toBeVisible();
  return page;
}

/** Resolves a staff task from the console task list with the given resolution label (and note). */
export async function resolveTask(page: Page, taskId: string, resolution: string, note?: string): Promise<void> {
  await page.goto("/console?tab=tasks");
  const row = page.getByTestId(`task-${taskId}`);
  await row.getByRole("button", { name: "Resolve" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Resolution").selectOption({ label: resolution });
  if (note !== undefined) {
    await dialog.getByLabel(/^Note/).fill(note);
  }
  await dialog.getByRole("button", { name: "Confirm resolve" }).click();
  await expect(dialog).toBeHidden();
  // "Remove from PACS" resolves the task only once the connector confirms the removal (GH-LIVE-02): reload until it is gone.
  await expect(async () => {
    await page.goto("/console?tab=tasks");
    await expect(page.getByRole("button", { name: "Open", exact: true })).toBeVisible({ timeout: 2_000 });
    await expect(page.getByText("Loading…")).toHaveCount(0, { timeout: 2_000 });
    await expect(row).toHaveCount(0, { timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
}
