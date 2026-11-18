import { expect, test } from "@playwright/test";
import { apiAs, endSponsorships, issueMagicLink, onboard } from "../fixtures/lifecycle";

const created: string[] = [];

test.afterAll(async ({ browser }) => {
  await endSponsorships(await apiAs(browser, "mikkel.holm"), created);
});

test("magic link signs in a guest once", async ({ browser }) => {
  const mikkel = await apiAs(browser, "mikkel.holm");
  const guest = await onboard(mikkel, { type: "Guest", givenName: "Gus", familyName: "Guest", days: 1 });
  created.push(guest.personId);
  const url = await issueMagicLink(mikkel, guest.personId);

  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto(url);
  await expect(page.getByRole("heading", { name: "Guest sign-in" })).toBeVisible();
  await page.getByRole("button", { name: "Continue as guest" }).click();
  await expect(page).toHaveURL(/\/map/);
  await expect(page.getByText("Gus Guest", { exact: true })).toBeVisible();
  const me = await page.request.get("/api/proxy/v1/me");
  expect(me.ok()).toBe(true);
  expect(((await me.json()) as { subject: string }).subject).toBe(`guest:${guest.personId}`);
  await context.close();
});

test("used magic link is refused", async ({ browser }) => {
  const mikkel = await apiAs(browser, "mikkel.holm");
  const guest = await onboard(mikkel, { type: "Guest", givenName: "Una", familyName: "Used", days: 1 });
  created.push(guest.personId);
  const url = await issueMagicLink(mikkel, guest.personId);

  const first = await browser.newContext();
  const firstPage = await first.newPage();
  await firstPage.goto(url);
  await firstPage.getByRole("button", { name: "Continue as guest" }).click();
  await expect(firstPage).toHaveURL(/\/map/);
  await first.close();

  const second = await browser.newContext();
  const page = await second.newPage();
  await page.goto(url);
  await page.getByRole("button", { name: "Continue as guest" }).click();
  await expect(page).toHaveURL(/\/guest\?error=invalid/);
  await expect(page.getByText("This link is invalid or has expired.")).toBeVisible();
  await second.close();
});
