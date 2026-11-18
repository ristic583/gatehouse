import { expect, test, type Browser, type Page } from "@playwright/test";
import { storageStateFor, type AuthUser } from "../fixtures/auth-state";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";

test.beforeEach(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

async function pageFor(browser: Browser, user: AuthUser): Promise<Page> {
  const context = await browser.newContext({ storageState: storageStateFor(user) });
  return context.newPage();
}

function inDays(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

async function annaRequestsServerRoom(page: Page): Promise<void> {
  await openFloor(page, "Second floor");
  await page.getByTestId("area-CPH-SERVER").click();
  await page.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Continue", exact: true }).click();
  await dialog.getByLabel("End date").fill(inDays(14));
  await dialog.getByLabel("Justification").fill("Rack migration for the new storage cluster");
  await dialog.getByRole("button", { name: "Review request" }).click();
  await dialog.getByRole("button", { name: "Send request" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId("area-CPH-SERVER")).toHaveAttribute("data-state", "Pending");
}

async function approveServerRoom(browser: Browser, user: AuthUser): Promise<void> {
  const page = await pageFor(browser, user);
  await page.goto("/approvals");
  const card = page.getByRole("article", { name: "Anna Larsen → Server Room" });
  await card.getByRole("button", { name: "Approve" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirm approval" }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(card).toHaveCount(0);
  await page.context().close();
}

test("high level request goes manager then owner and stays pending", async ({ browser }) => {
  const anna = await pageFor(browser, "anna.larsen");
  await annaRequestsServerRoom(anna);

  await approveServerRoom(browser, "mikkel.holm");
  await approveServerRoom(browser, "sofie.berg");

  await openFloor(anna, "Second floor");
  const server = anna.getByTestId("area-CPH-SERVER");
  await expect(server).toHaveAttribute("data-state", "Pending");
  await expect(server).not.toHaveAttribute("data-state", "HasAccess");
  await anna.context().close();
});

test("requester never sees own request in inbox", async ({ browser }) => {
  const anna = await pageFor(browser, "anna.larsen");
  await annaRequestsServerRoom(anna);

  await anna.goto("/approvals");
  await expect(anna.getByRole("heading", { name: "Good access starts with you." })).toBeVisible();
  await expect(anna.getByRole("article", { name: "Anna Larsen → Server Room" })).toHaveCount(0);
  await anna.context().close();
});
