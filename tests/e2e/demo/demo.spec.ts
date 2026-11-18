import { expect, test, type Browser, type Page, type TestInfo } from "@playwright/test";
import { storageStateFor, type AuthUser } from "../fixtures/auth-state";
import { swipe } from "../fixtures/building";
import { inDays } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { runReconciliation } from "../fixtures/reconciliation";
import { resetDemoAccess } from "../fixtures/reset";

/**
 * The 3-minute demo (spec §17) on the real UI, recorded per persona (`npx playwright test --project=demo`).
 * Steps 6-7 of the script (stopping the connector container) are not automated: stopping containers from a test is unsafe.
 */
test.setTimeout(300_000);

const SIZE = { width: 1280, height: 800 };

async function recorded(browser: Browser, user: AuthUser, info: TestInfo): Promise<Page> {
  const context = await browser.newContext({ storageState: storageStateFor(user), viewport: SIZE, recordVideo: { dir: info.outputPath(`video-${user}`), size: SIZE } });
  return context.newPage();
}

async function approve(page: Page, card: string): Promise<void> {
  await page.goto("/approvals");
  const item = page.getByRole("article", { name: card });
  await item.getByRole("button", { name: "Approve" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirm approval" }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(item).toHaveCount(0);
}

test("demo: request, approve, apply, live door, reconciliation, audit, second tenant", async ({ browser }, info) => {
  await resetDemoAccess(browser, "nordlys", "Connector");

  // 1-2: Anna requests the Server Room for two weeks.
  const anna = await recorded(browser, "anna.larsen", info);
  await openFloor(anna, "Second floor");
  await anna.getByTestId("area-CPH-SERVER").click();
  await anna.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  const dialog = anna.getByRole("dialog");
  await dialog.getByRole("button", { name: "Continue", exact: true }).click();
  await dialog.getByLabel("End date").fill(inDays(14));
  await dialog.getByLabel("Justification").fill("Rack migration for the new storage cluster");
  await dialog.getByRole("button", { name: "Review request" }).click();
  await dialog.getByRole("button", { name: "Send request" }).click();
  await expect(dialog).toBeHidden();
  await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "Pending");

  // 3: manager and area owner approve; still pending until the PACS confirms.
  const mikkel = await recorded(browser, "mikkel.holm", info);
  await approve(mikkel, "Anna Larsen → Server Room");
  const sofie = await recorded(browser, "sofie.berg", info);
  await approve(sofie, "Anna Larsen → Server Room");

  // 4: the HQ agent applies the change to LegacyPACS; the ack turns the map green.
  await expect(async () => {
    await openFloor(anna, "Second floor");
    await expect(anna.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
  }).toPass({ timeout: 90_000 });

  // 5: live building view; a swipe at the Server Room is decided by the PACS controller and shows up live.
  const jonas = await recorded(browser, "jonas.nielsen", info);
  await jonas.goto("/live");
  await expect(jonas.getByTestId("live-status")).toHaveAttribute("data-status", "connected", { timeout: 20_000 });
  await jonas.getByRole("combobox", { name: "Floor" }).selectOption({ label: "Second floor" });
  await swipe(jonas.request, "D-CPH-SERVER", "Anna Larsen");
  await jonas.waitForTimeout(4_000);

  // How it works: the same flows, named after the real components.
  await jonas.goto("/how-it-works");
  await expect(jonas.getByTestId("lane-door")).toBeVisible();
  await jonas.waitForTimeout(4_000);

  // 8: reconciliation of the CloudPACS warehouse.
  await runReconciliation(jonas, "Warehouse-Aarhus");

  // 9: audit chain verified.
  await jonas.goto("/console?tab=audit");
  await jonas.getByRole("button", { name: "Verify audit chain" }).click();
  await expect(jonas.getByText(/Audit chain verified/)).toBeVisible({ timeout: 20_000 });

  // Dashboard of what the app already tracks.
  await jonas.goto("/dashboard");
  await expect(jonas.getByTestId("tile-tasks")).toBeVisible();
  await jonas.waitForTimeout(2_000);

  // 10: Bricks GmbH (SAML): its own theme and terminology, zero overlap with Nordlys.
  const lena = await recorded(browser, "lena.schmidt", info);
  await lena.goto("/map");
  await expect(lena.getByTestId("current-tenant")).toHaveText(/Bricks/);
  await lena.waitForTimeout(3_000);

  for (const page of [anna, mikkel, sofie, jonas, lena]) {
    await page.context().close();
  }
  await resetDemoAccess(browser, "nordlys");
});
