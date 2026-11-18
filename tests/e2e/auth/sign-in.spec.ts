import { expect, test } from "@playwright/test";
import { demoUsers } from "../fixtures/demo-users";
import { signIn } from "../fixtures/sign-in";

test("nordlys employee signs in through the OIDC broker", async ({ page }) => {
  await signIn(page, demoUsers.annaLarsen);

  await expect(page.getByTestId("current-tenant")).toHaveText(/Nordlys A\/S/);
});

test("bricks employee signs in through the SAML broker", async ({ page }) => {
  await signIn(page, demoUsers.lenaSchmidt);

  await expect(page.getByTestId("current-tenant")).toHaveText(/Bricks GmbH/);
});

test("signed-out visitor starts at the app email screen", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await page.goto("/");

  await expect(page).toHaveURL(/\/login(?:\?|$)/);
  await expect(page.getByLabel("Work email")).toBeVisible();
  await context.close();
});

test("sign out ends the session", async ({ page }) => {
  await signIn(page, demoUsers.annaLarsen);
  await expect(page.getByTestId("current-tenant")).toBeVisible();

  await page.getByRole("button", { name: "Sign out" }).click();
  await page.waitForURL(/localhost:3000\/|localhost:8080\//);

  await page.goto("/");
  await expect(page).toHaveURL(/\/login(?:\?|$)/);
  await expect(page.getByLabel("Work email")).toBeVisible();
});


test("manager enters the approval inbox", async ({ page }) => {
  await signIn(page, demoUsers.mikkelHolm);
  await expect(page).toHaveURL(/\/approvals(?:\?|$)/);
  await expect(page.getByRole("heading", { name: "Good access starts with you." })).toBeVisible();
});

test("security staff enter operations", async ({ page }) => {
  await signIn(page, demoUsers.jonasNielsen);
  await expect(page).toHaveURL(/\/dashboard(?:\?|$)/);
});
